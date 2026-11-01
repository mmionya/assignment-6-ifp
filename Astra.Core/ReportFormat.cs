namespace Astra.Core;

public static class ReportFormat
{
    public static IEnumerable<string> ToCsvLines(ReportSet reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        return CsvIterator(reports);
    }

    private static IEnumerable<string> CsvIterator(ReportSet reports)
    {
        yield return "Report,Quote list";
        yield return "Id,Destination,Passengers,TravelClass,TotalUsd";
        foreach (var quote in reports.Quotes.Quotes)
            yield return string.Join(',',  Escape(quote.Booking.Id), quote.Booking.Destination, Number(quote.Booking.Passengers), quote.Booking.TravelClass, Money(quote.TotalUsd));
        yield return $"Total,,,,{Money(reports.Quotes.TotalUsd)}";
        yield return "";
        yield return "Report,Demand by destination";
        yield return "Destination,Bookings,Passengers,RevenueUsd";
        foreach (var line in reports.Demand.Lines)
            yield return string.Join(',', line.Destination, Number(line.Bookings), Number(line.Passengers), Money(line.RevenueUsd));
        yield return $"Total,,,{Money(reports.Demand.TotalUsd)}";
    }
    public static string Money(decimal value) => FormattableString.Invariant($"{value:F2}");
    private static string Number(int value) => FormattableString.Invariant($"{value}");
    private static string Escape(string field) => field.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
}