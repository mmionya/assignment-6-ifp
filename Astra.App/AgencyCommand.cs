using System.Globalization;
using System.Text;
using Astra.Core;
using Astra.Core.Reservations;

namespace Astra.App;

public static class AgencyCommand
{
    public const string Usage = """
        ASTRA agency commands (dates: yyyy-MM-dd; classes: explorer|premium|first):
          agency flights <state.json> <as-of>
          agency quote <state.json> <flight> <passengers> <class> <standard|group10|family5> <as-of>
          agency reserve <state.json> <booking-id> <flight> <passengers> <class> <policy> <as-of>
          agency cancel <state.json> <booking-id> <as-of>
          agency transfer <state.json> <booking-id> <new-flight> <class> <policy> <as-of>
          agency find <state.json> <booking-id>
          agency reports <state.json> <as-of> [export.csv]
        Reserve, cancel and transfer save the new state only after all rules pass.
        Amounts are contractual quotes/refunds; no bank payment is made.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is [] or ["help"] or ["--help"])
        {
            output.WriteLine(Usage);
            return 0;
        }
        return ParseCommand(args).Match(
            execute => execute().Match(message => { output.WriteLine(message); return 0; },
                message => { error.WriteLine(message); return 1; }),
            message => { error.WriteLine(message); return 2; });
    }

    // Parse all syntax before opening a file or acquiring a state lock. The returned
    // function performs the business operation only after argument parsing succeeds.
    private static Result<Func<Result<string>>> ParseCommand(string[] args) => args switch
    {
        ["flights", var path, var date] => Date(date).Map<Func<Result<string>>>(asOf => () => ReservationStore.Load(path)
            .Map(state => RenderFlights(AgencyReports.Build(state, asOf)))),
        ["quote", var path, var flight, var count, var travelClass, var policy, var date] =>
            Selection(count, travelClass, policy, date).Map<Func<Result<string>>>(input => () => ReservationStore.Load(path)
                .Bind(state => ReservationBooking.Quote(state, flight, input.Passengers,
                    input.TravelClass, input.AsOf, input.Policy))
                .Map(quote => $"Quote: {quote.Flight.Id}; {quote.Passengers} passenger(s); {quote.TravelClass}; {Money(quote.TotalUsd)} USD")),
        ["reserve", var path, var id, var flight, var count, var travelClass, var policy, var date] =>
            Selection(count, travelClass, policy, date).Map<Func<Result<string>>>(input => () => ReservationStore.Update(path,
                state => ReservationBooking.Reserve(state, id, flight, input.Passengers,
                    input.TravelClass, input.AsOf, input.Policy)).Map(change => RenderChange("Reserved", change))),
        ["cancel", var path, var id, var date] => Date(date).Map<Func<Result<string>>>(asOf => () => ReservationStore.Update(path,
            state => ReservationChanges.Cancel(state, id, asOf)).Map(change => RenderChange("Cancelled", change))),
        ["transfer", var path, var id, var flight, var travelClass, var policy, var date] =>
            Selection("1", travelClass, policy, date).Map<Func<Result<string>>>(input => () => ReservationStore.Update(path,
                state => ReservationChanges.Transfer(state, id, flight, input.TravelClass,
                    input.AsOf, input.Policy)).Map(change => RenderChange("Transferred", change))),
        ["find", var path, var id] => Result<Func<Result<string>>>.Success(() =>
            ReservationStore.Load(path).Bind(state => state.FindReservation(id) switch
        {
            Option<Reservation>.Some found => Result<string>.Success(RenderBooking(found.Value)),
            _ => Result<string>.Failure($"Reservation '{id}' was not found.")
        })),
        ["reports", var path, var date] => Date(date)
            .Map<Func<Result<string>>>(asOf => () => Reports(path, asOf, null)),
        ["reports", var path, var date, var export] => Date(date)
            .Map<Func<Result<string>>>(asOf => () => Reports(path, asOf, export)),
        _ => Result<Func<Result<string>>>.Failure(Usage)
    };

    private sealed record InputSelection(int Passengers, TravelClass TravelClass,
        Func<Flight, int, TravelClass, decimal> Policy, DateOnly AsOf);

    private static Result<InputSelection> Selection(string count, string travelClass, string policy, string date)
    {
        if (!int.TryParse(count, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var passengers))
            return Result<InputSelection>.Failure("Passengers must be an integer from 1 to 6.");
        return Class(travelClass).Bind(selectedClass => Policy(policy)
            .Bind(selectedPolicy => Date(date).Map(asOf => new InputSelection(passengers, selectedClass, selectedPolicy, asOf))));
    }

    private static Result<DateOnly> Date(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? Result<DateOnly>.Success(date) : Result<DateOnly>.Failure("Date must have format yyyy-MM-dd.");

    private static Result<TravelClass> Class(string value) => value.ToLowerInvariant() switch
    {
        "explorer" => Result<TravelClass>.Success(TravelClass.Explorer),
        "premium" => Result<TravelClass>.Success(TravelClass.Premium),
        "first" => Result<TravelClass>.Success(TravelClass.First),
        _ => Result<TravelClass>.Failure("Class must be explorer, premium or first.")
    };

    private static Result<Func<Flight, int, TravelClass, decimal>> Policy(string value) => value.ToLowerInvariant() switch
    {
        "standard" => Result<Func<Flight, int, TravelClass, decimal>>.Success(ReservationPricing.Standard),
        "group10" => Result<Func<Flight, int, TravelClass, decimal>>.Success(ReservationPricing.Group10),
        "family5" => Result<Func<Flight, int, TravelClass, decimal>>.Success(ReservationPricing.Family5),
        _ => Result<Func<Flight, int, TravelClass, decimal>>.Failure("Policy must be standard, group10 or family5.")
    };

    private static string RenderBooking(Reservation b) =>
        $"{b.Id}: {b.Status}; flight {b.FlightId}; {b.Passengers} passenger(s); {b.TravelClass}; " +
        $"contract {Money(b.TotalUsd)} USD; refund {Money(b.RefundUsd)} USD";

    private static string RenderChange(string action, ReservationChange change) =>
        $"{action}. {RenderBooking(change.Booking)}{Environment.NewLine}" +
        $"Balance due: {Money(change.BalanceDueUsd)} USD (negative means refund). State saved.";

    private static string RenderFlights(AgencyReportSet report)
    {
        var text = new StringBuilder("Flight availability\n");
        foreach (var line in report.Occupancy)
            text.AppendLine($"{line.FlightId}: {line.Destination}, {IsoDate(line.Departure)}; " +
                $"{line.FreeSeats}/{line.Capacity} seats available; {Money(line.OccupancyPercent)}% occupied");
        if (report.Occupancy.IsEmpty) text.AppendLine("No upcoming bookable flights.");
        return text.ToString().TrimEnd();
    }

    private static Result<string> Reports(string path, DateOnly asOf, string? export) =>
        ReservationStore.Load(path).Bind(state =>
        {
            var reports = AgencyReports.Build(state, asOf);
            var native = new NativeGroupEligibility();
            var eligible = 0;
            foreach (var booking in state.Reservations.Where(b => b.Status == ReservationStatus.Confirmed))
            {
                var outcome = native.IsEligible(booking.Passengers, 4);
                if (!outcome.IsSuccess) return Result<string>.Failure(outcome.Error!);
                if (outcome.Value) eligible++;
            }
            var text = new StringBuilder(RenderFlights(reports)).AppendLine().AppendLine("Contract value by destination");
            foreach (var line in reports.Finance)
                text.AppendLine($"{line.Destination}: confirmed {line.Confirmed}, cancelled {line.Cancelled}; " +
                    $"contract {Money(line.ContractUsd)}, refunds {Money(line.RefundUsd)}, retained {Money(line.RetainedUsd)} USD");
            text.AppendLine($"Total retained contract value: {Money(reports.Finance.Sum(l => l.RetainedUsd))} USD");
            text.AppendLine($"Native C: {eligible} confirmed booking(s) qualify for the 4+ passenger group threshold.");
            var result = text.ToString().TrimEnd();
            return export is null ? Result<string>.Success(result)
                : ReportFiles.ValidateDestination(export, path)
                    .Bind(destination => ReportFiles.Export(destination, Csv(reports)))
                    .Map(_ => result + Environment.NewLine + $"Exported: {export}");
        });

    private static IEnumerable<string> Csv(AgencyReportSet reports)
    {
        yield return "Report,Flight availability";
        yield return "Flight,Destination,Departure,Capacity,Reserved,Free,OccupancyPercent";
        foreach (var line in reports.Occupancy)
            yield return $"{CsvCell(line.FlightId)},{line.Destination},{IsoDate(line.Departure)},{line.Capacity},{line.ReservedSeats},{line.FreeSeats},{Money(line.OccupancyPercent)}";
        yield return "";
        yield return "Report,Contract value by destination";
        yield return "Destination,Confirmed,Cancelled,ContractUsd,RefundUsd,RetainedUsd";
        foreach (var line in reports.Finance)
            yield return $"{line.Destination},{line.Confirmed},{line.Cancelled},{Money(line.ContractUsd)},{Money(line.RefundUsd)},{Money(line.RetainedUsd)}";
    }

    private static string CsvCell(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string IsoDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
}
