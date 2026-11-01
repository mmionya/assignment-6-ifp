using System.Globalization;
using Astra.Core;

namespace Astra.App;

public static class ReportCommand
{
    private static readonly Dictionary<string, Func<BookingRequest, decimal>> Policies = new()
    {
        ["standard"] = BookingPipeline.Standard,
        ["group10"] = Pricing.Group10,
        ["family5"] = Pricing.Family5
    };

    public static int Run(string[] args, TextWriter output, TextWriter error, IGroupEligibility? native = null)
    {
        if (args.Length is not (3 or 4) || !DateOnly.TryParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf) || !Policies.TryGetValue(args[2], out var price))
        {
            error.WriteLine("Usage: dotnet run --project Astra.App -- report <input.json> <as-of:yyyy-MM-dd> <standard|group10|family5> [export.csv]");
            return 2;
        }

        var inputPath = args[0];
        var exportPath = args.Length == 4 ? args[3] : null;

        return BookingFiles.Load(inputPath, json => Input.Parse(json)
                .Bind(Input.Validate)
                .Map(batch => BookingReports.Build(batch, asOf, price)))
                .Bind(reports => NativeReportCalculation.CountEligible(reports.Quotes, native ?? new NativeGroupEligibility())
                    .Map(summary => (Reports: reports, Native: summary)))
                .Bind(result => Export(result.Reports, inputPath, exportPath)
                    .Map(_ => result))
                .Match(result => Print(output, result.Reports, result.Native, args[2], asOf, exportPath), message =>
                {
                    error.WriteLine(message);
                    return 1;
                });
    }

    private static Result<ReportSet> Export(ReportSet reports, string inputPath, string? exportPath)
    {
        if (exportPath is null)
            return Result<ReportSet>.Success(reports);
        if (Path.GetFullPath(exportPath) == Path.GetFullPath(inputPath))
            return Result<ReportSet>.Failure("Export path must differ from the input file; the input is never overwritten.");
        return ReportFiles.Export(exportPath, ReportFormat.ToCsvLines(reports)).Map(_ => reports);
    }

    private static int Print(TextWriter output, ReportSet reports, NativeGroupSummary native, string policy, DateOnly asOf, string? exportPath)
    {
        output.WriteLine($"Policy: {policy}; As of: {asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        output.WriteLine("Report 1: Quote list");
        foreach (var quote in reports.Quotes.Quotes)
            output.WriteLine($"{quote.Booking.Id}: {quote.Booking.Destination}, {quote.Booking.Passengers} passenger(s), " + $"{quote.Booking.TravelClass} => {ReportFormat.Money(quote.TotalUsd)} USD");
        output.WriteLine($"Accepted: {reports.Quotes.Quotes.Length}; Total: {ReportFormat.Money(reports.Quotes.TotalUsd)} USD");

        output.WriteLine("Report 2: Demand by destination");
        foreach (var line in reports.Demand.Lines)
            output.WriteLine($"{line.Destination}: {line.Bookings} booking(s), {line.Passengers} passenger(s) " + $"=> {ReportFormat.Money(line.RevenueUsd)} USD");
        output.WriteLine($"Total: {ReportFormat.Money(reports.Demand.TotalUsd)} USD");

        output.WriteLine($"Native C: {native.EligibleBookings} accepted booking(s) have at least {native.MinimumPassengers} passengers.");
        if (exportPath is not null) output.WriteLine($"Exported: {exportPath}");
        return 0;
    }
}