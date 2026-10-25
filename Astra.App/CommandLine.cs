using System.Collections.Immutable;
using System.Globalization;
using Astra.Core;

namespace Astra.App;

public static class CommandLine
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is ["demo"])
            return TemporalDemo.Run();

        if (args is ["--help"] or ["-h"] or ["help"])
        {
            PrintUsage(output);
            output.WriteLine("The review date and pricing policy are explicit inputs.");
            output.WriteLine("find validates the whole file and distinguishes a missing booking from Cancelled=False.");
            output.WriteLine("demo shows safe states, rejected transitions, and repeated calculation.");
            output.WriteLine("Quotes are calculated in memory; no booking is submitted or saved.");
            output.WriteLine("Exit codes: 0 = success/help; 1 = file, parsing, or domain failure; 2 = invalid arguments.");
            return 0;
        }

        if (args is ["find", var findPath, var bookingId])
        {
            return BookingFiles.FindBooking(findPath, bookingId).Match(
                booking =>
                {
                    output.WriteLine($"Found booking: {booking.Id}; Destination: {booking.Destination}; " +
                        $"Passengers: {booking.Passengers}; Cancelled: {booking.IsCancelled}");
                    return 0;
                },
                message => Fail(error, message));
        }

        if (args.Length != 3 || args[1] is not ("standard" or "group" or "both") ||
            !DateOnly.TryParseExact(args[2], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var asOf))
        {
            PrintUsage(error);
            return 2;
        }

        var policies = args[1] == "both" ? new[] { "standard", "group" } : [args[1]];
        // Read one snapshot and finish every calculation before printing success.
        return BookingFiles.Load(args[0], json => BuildReports(json, asOf, policies)).Match(
            reports => PrintReports(output, reports, asOf),
            message => Fail(error, message));
    }

    private sealed record PolicyReport(string Policy, QuoteSummary Summary);

    private static Result<ImmutableArray<PolicyReport>> BuildReports(string json,
        DateOnly asOf, IEnumerable<string> policies)
    {
        var result = Result<ImmutableArray<PolicyReport>>.Success([]);
        foreach (var name in policies)
        {
            Func<BookingRequest, decimal> policy = name == "standard"
                ? BookingPipeline.Standard : BookingPipeline.GroupDiscount;
            result = result.Bind(reports => Input.Process(json, asOf, policy)
                .Map(summary => reports.Add(new PolicyReport(name, summary))));
        }
        return result;
    }

    private static int PrintReports(TextWriter output, ImmutableArray<PolicyReport> reports, DateOnly asOf)
    {
        foreach (var report in reports)
        {
            output.WriteLine($"Policy: {report.Policy}; As of: {asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
            foreach (var quote in report.Summary.Quotes)
                output.WriteLine(FormattableString.Invariant(
                    $"{quote.Booking.Id}: {quote.Booking.Destination}, {quote.Booking.Passengers} passenger(s), {quote.Booking.TravelClass} => {quote.TotalUsd:F2} USD"));
            output.WriteLine(FormattableString.Invariant(
                $"Accepted: {report.Summary.Quotes.Length}; Total: {report.Summary.TotalUsd:F2} USD"));
        }
        return 0;
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 1;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: dotnet run --project Astra.App -- <input.json> <standard|group|both> <as-of:yyyy-MM-dd>");
        writer.WriteLine("       dotnet run --project Astra.App -- find <input.json> <booking-id>");
        writer.WriteLine("       dotnet run --project Astra.App -- demo");
        writer.WriteLine("       dotnet run --project Astra.App -- --help");
    }
}
