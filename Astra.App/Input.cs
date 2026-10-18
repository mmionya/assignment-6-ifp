using System.Text.Json;
using System.Text.Json.Serialization;
using Astra.Core;

namespace Astra.App;

public static class Input
{
    public static Result<RawBooking?[]> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result<RawBooking?[]>.Failure("Input is missing; use [] for an empty dataset.");
        try
        {
            var rows = JsonSerializer.Deserialize<RawBooking?[]>(json, new JsonSerializerOptions
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                AllowDuplicateProperties = false,
                Converters =
                {
                    new JsonStringEnumConverter<Destination>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
                    new JsonStringEnumConverter<TravelClass>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
                }
            });
            return rows is null
                ? Result<RawBooking?[]>.Failure("Input must be an array, not null.")
                : Result<RawBooking?[]>.Success(rows);
        }
        catch (JsonException ex)
        {
            return Result<RawBooking?[]>.Failure($"Invalid JSON: {ex.Message}");
        }
    }

    public static Result<ValidatedBookingBatch> Validate(RawBooking?[]? rows) =>
        ValidatedBookingBatch.Create(rows);

    public static Result<QuoteSummary> Process(string? json, DateOnly asOf, Func<BookingRequest, decimal> policy) =>
        Parse(json)
            .Bind(rows => QuoteWorkflow.Prepare(new QuoteState.Pending(rows, asOf, policy)))
            .Bind(ready => QuoteWorkflow.Calculate(ready))
            .Map(completed => completed.Summary);
}
