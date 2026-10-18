using System.Globalization;
using Astra.App;
using Astra.Core;

if (args is ["demo"])
    return TemporalDemo.Run();

if (args is ["--help"] or ["-h"] or ["help"])
{
    Console.WriteLine("Usage: dotnet run --project Astra.App -- <input.json> <standard|group|both> <as-of:yyyy-MM-dd>");
    Console.WriteLine("       dotnet run --project Astra.App -- demo");
    Console.WriteLine("       dotnet run --project Astra.App -- --help");
    Console.WriteLine("The original command calculates quotes using an explicit review date and pricing policy.");
    Console.WriteLine("The demo shows Pending -> Ready -> Completed, rejected transitions, and repeated calculation.");
    Console.WriteLine("Quotes are calculated in memory; no booking is submitted or saved.");
    Console.WriteLine("Exit codes: 0 = success; 1 = input/validation failure; 2 = invalid command arguments.");
    return 0;
}

if (args.Length != 3 || args[1] is not ("standard" or "group" or "both") ||
    !DateOnly.TryParseExact(args[2], "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var asOf))
{
    Console.Error.WriteLine("Usage: dotnet run --project Astra.App -- <input.json> <standard|group|both> <as-of:yyyy-MM-dd>");
    return 2;
}

string json;
try { json = File.ReadAllText(args[0]); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine($"Cannot read input: {ex.Message}");
    return 1;
}

var policies = args[1] == "both" ? new[] { "standard", "group" } : new[] { args[1] };
foreach (var name in policies)
{
    Func<BookingRequest, decimal> policy = name == "standard" ? BookingPipeline.Standard : BookingPipeline.GroupDiscount;
    var result = Input.Process(json, asOf, policy);
    if (!result.IsSuccess)
    {
        Console.Error.WriteLine(result.Error);
        return 1;
    }
    Console.WriteLine($"Policy: {name}; As of: {asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
    foreach (var quote in result.Value.Quotes)
        Console.WriteLine(FormattableString.Invariant(
            $"{quote.Booking.Id}: {quote.Booking.Destination}, {quote.Booking.Passengers} passenger(s), {quote.Booking.TravelClass} => {quote.TotalUsd:F2} USD"));
    Console.WriteLine(FormattableString.Invariant($"Accepted: {result.Value.Quotes.Length}; Total: {result.Value.TotalUsd:F2} USD"));
}
return 0;
