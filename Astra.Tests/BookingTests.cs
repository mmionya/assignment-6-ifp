using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Astra.App;
using Astra.Core;
using Xunit;

namespace Astra.Tests;

public class BookingTests
{
    private readonly DateOnly asOf = new(2030, 6, 15);
    private readonly string json = Sample("bookings");
    private readonly ValidatedBookingBatch batch = Input.Parse(Sample("bookings")).Bind(Input.Validate).Value;
    private ImmutableArray<BookingRequest> bookings => batch.Bookings;
    private BookingRequest moon => bookings[0];

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", name + ".json"));

    private static void Fails<T>(Result<T> result)
    {
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void NormalWorkflow()
    {
        var result = Input.Process(json, asOf, BookingPipeline.Standard).Value;
        Assert.Equal(new[] { "A", "B", "F", "C" }, result.Quotes.Select(q => q.Booking.Id));
        Assert.Equal(new[] { 49_000m, 264_600m, 7_200_000m, 25_920_000m }, result.Quotes.Select(q => q.TotalUsd));
        Assert.Equal(33_433_600m, result.TotalUsd);
    }

    [Fact]
    public void EmptyDataset()
    {
        foreach (var policy in new Func<BookingRequest, decimal>[] { BookingPipeline.Standard, BookingPipeline.GroupDiscount })
        {
            var summary = Input.Process(Sample("empty"), asOf, policy).Value;
            Assert.Empty(summary.Quotes);
            Assert.Equal(0m, summary.TotalUsd);
        }
    }

    [Fact]
    public void DepartureBoundary()
    {
        Assert.Equal(4, BookingPipeline.SelectBookable(bookings, asOf).Length);
        Assert.Equal(new[] { "C", "F" }, BookingPipeline.SelectBookable(bookings, asOf.AddDays(1))
            .Select(b => b.Id));
        Assert.Empty(BookingPipeline.Run(batch, new(2032, 1, 1), BookingPipeline.Standard).Quotes);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, true)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(int.MaxValue, false)]
    public void PassengerBoundaries(int count, bool valid)
    {
        var row = Input.Parse(json).Value[0] ?? throw new Exception("Missing sample row");
        var result = Input.Validate(new[] { row with { Passengers = count } });
        Assert.Equal(valid, result.IsSuccess);
        if (!valid) Fails(result);
    }

    [Fact]
    public void TwoPolicies()
    {
        var standard = Input.Process(json, asOf, BookingPipeline.Standard).Value;
        var group = Input.Process(json, asOf, BookingPipeline.GroupDiscount).Value;
        Assert.Equal(new[] { 49_000m, 238_140m, 7_200_000m, 23_328_000m }, group.Quotes.Select(q => q.TotalUsd));
        Assert.Equal(30_815_140m, group.TotalUsd);
        Assert.NotEqual(standard.TotalUsd, group.TotalUsd);
        Assert.Equal(147_000m, BookingPipeline.GroupDiscount(moon with { Passengers = 3 }));
        Assert.Equal(176_400m, BookingPipeline.GroupDiscount(moon with { Passengers = 4 }));
    }

    [Fact]
    public void InvalidBatch()
    {
        Fails(Input.Process(Sample("invalid"), asOf, BookingPipeline.Standard));
    }

    [Fact]
    public void MalformedInput()
    {
        foreach (var bad in new[] { "{", "{}", "42", "[true]", Sample("malformed") })
            Fails(Input.Process(bad, asOf, BookingPipeline.Standard));
        var row = JsonNode.Parse(json)?[0]?.DeepClone() ?? throw new Exception("Missing sample");
        foreach (var (field, value) in new[] { ("Destination", "saturn"), ("TravelClass", "business"), ("Departure", "2030-02-30") })
        {
            var changed = row.DeepClone();
            changed[field] = value;
            Fails(Input.Parse(new JsonArray(changed).ToJsonString()));
        }
        foreach (var field in new[] { "Destination", "TravelClass" })
        {
            var changed = row.DeepClone();
            changed[field] = 0;
            Fails(Input.Parse(new JsonArray(changed).ToJsonString()));
        }
    }

    [Fact]
    public void MissingDataset()
    {
        foreach (var bad in new string?[] { null, "", " ", "null", "[null]", "[{}]", Sample("missing") })
            Fails(Input.Process(bad, asOf, BookingPipeline.Standard));
        Fails(Input.Validate(null));
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Destination")]
    [InlineData("Passengers")]
    [InlineData("TravelClass")]
    [InlineData("Departure")]
    [InlineData("IsCancelled")]
    public void RequiredFields(string field)
    {
        var row = JsonNode.Parse(json)?[0]?.DeepClone().AsObject() ?? throw new Exception("Missing sample");
        row.Remove(field);
        Fails(Input.Process(new JsonArray(row.DeepClone()).ToJsonString(), asOf, BookingPipeline.Standard));
        row[field] = null;
        Fails(Input.Process(new JsonArray(row).ToJsonString(), asOf, BookingPipeline.Standard));
    }

    [Fact]
    public void FalseIsValid()
    {
        Assert.False(bookings[0].IsCancelled);
        Assert.Single(BookingPipeline.SelectBookable(ImmutableArray.Create(moon), asOf));
    }

    [Fact]
    public void InvalidFields()
    {
        var row = Input.Parse(json).Value[0] ?? throw new Exception("Missing sample");
        foreach (var id in new[] { " ", new string('x', 41), "bad\nID" })
            Fails(Input.Validate(new[] { row with { Id = id } }));
        Fails(Input.Validate(new[] { row, row with { Id = " A " } }));
        Fails(Input.Validate(new[] { row with { Departure = asOf.AddDays(1) } }));
        Fails(Input.Validate(new[] { row with { Destination = (Destination)99 } }));
        Fails(Input.Validate(new[] { row with { TravelClass = (TravelClass)99 } }));
    }

    [Fact]
    public void InvalidProperties()
    {
        Fails(Input.Parse("[{\"Extra\":true}]"));
        Fails(Input.Parse("[{\"Id\":\"A\",\"Id\":\"B\"}]"));
    }

    [Fact]
    public void SelectionStage()
    {
        Assert.Equal(new[] { "A", "B", "C", "F" },
            BookingPipeline.SelectBookable(bookings, asOf).Select(b => b.Id));
    }

    [Fact]
    public void PolicyInvocation()
    {
        var calls = new List<string>();
        var summary = BookingPipeline.Run(batch, asOf, b => { calls.Add(b.Id); return 12.345m; });
        Assert.Equal(new[] { "A", "B", "C", "F" }, calls);
        Assert.All(summary.Quotes, q => Assert.Equal(12.35m, q.TotalUsd));
        Assert.Equal(49.40m, summary.TotalUsd);
    }

    [Fact]
    public void SummaryStage()
    {
        var quotes = ImmutableArray.Create(new BookingQuote(moon with { Id = "Z" }, 100m), new BookingQuote(moon, 100m));
        var summary = BookingPipeline.Summarize(quotes);
        Assert.Equal("A", summary.Quotes[0].Booking.Id);
        Assert.Equal("Z", quotes[0].Booking.Id);
        Assert.Equal(200m, summary.TotalUsd);
    }

    [Fact]
    public void Purity()
    {
        var rows = Input.Parse(json).Value;
        var first = rows[0] ?? throw new Exception("Missing sample");
        rows[0] = first with { Id = " A " };
        var rawSnapshot = rows.ToArray();
        var validated = Input.Validate(rows).Value;
        Assert.Equal(rawSnapshot, rows);
        Assert.Equal(" A ", rows[0]?.Id);
        Assert.Equal("A", validated.Bookings[0].Id);
        rows[0] = first with { Passengers = 6 };
        Assert.Equal(1, validated.Bookings[0].Passengers);
        var snapshot = validated.Bookings.ToArray();
        var result1 = BookingPipeline.Run(validated, asOf, BookingPipeline.Standard);
        var result2 = BookingPipeline.Run(validated, asOf, BookingPipeline.Standard);
        Assert.Equal(snapshot, validated.Bookings);
        Assert.Equal<BookingQuote>(result1.Quotes, result2.Quotes);
        Assert.Equal(result1.TotalUsd, result2.TotalUsd);
    }

    [Fact]
    public void MapAndBind()
    {
        Assert.Equal(10, Result<int>.Success(4).Map(n => n + 1).Bind(n => Result<int>.Success(n * 2)).Value);
        var failure = Result<int>.Success(4).Bind(_ => Result<int>.Failure("denied"));
        Fails(failure);
        Assert.Equal("denied", failure.Error);
    }

    [Fact]
    public void ParseFailureSkipsCallbacks()
    {
        var validations = 0;
        var pipelines = 0;
        var parsed = Input.Parse("{");
        var result = parsed.Bind(rows => { validations++; return Input.Validate(rows); })
            .Map(rows => { pipelines++; return BookingPipeline.Run(rows, asOf, BookingPipeline.Standard); });
        Fails(result);
        Assert.Equal(parsed.Error, result.Error);
        Assert.Equal(0, validations);
        Assert.Equal(0, pipelines);
    }

    [Fact]
    public void ValidationFailureSkipsCallbacks()
    {
        var pipelines = 0;
        var policyCalls = 0;
        var result = Input.Parse(Sample("invalid")).Bind(Input.Validate)
            .Map(rows => { pipelines++; return BookingPipeline.Run(rows, asOf, BookingPipeline.Standard); });
        Fails(result);
        Fails(Input.Process(Sample("invalid"), asOf, _ => { policyCalls++; return 1m; }));
        Assert.Equal(0, pipelines);
        Assert.Equal(0, policyCalls);
    }
}
