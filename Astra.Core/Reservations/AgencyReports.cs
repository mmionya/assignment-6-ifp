using System.Collections.Immutable;

namespace Astra.Core.Reservations;

public sealed record OccupancyLine(string FlightId, Destination Destination, DateOnly Departure,
    int Capacity, int ReservedSeats, int FreeSeats, decimal OccupancyPercent);
public sealed record FinanceLine(Destination Destination, int Confirmed, int Cancelled,
    decimal ContractUsd, decimal RefundUsd, decimal RetainedUsd);
public sealed record AgencyReportSet(ImmutableArray<OccupancyLine> Occupancy,
    ImmutableArray<FinanceLine> Finance);

public static class AgencyReports
{
    public static AgencyReportSet Build(ReservationState state, DateOnly asOf)
    {
        var occupancy = state.Flights
            .Where(f => f.IsBookable && f.Departure > asOf)
            .Select(f =>
            {
                var free = state.AvailableSeats(f.Id);
                return new OccupancyLine(f.Id, f.Destination, f.Departure, f.Capacity,
                    f.Capacity - free, free,
                    decimal.Round((f.Capacity - free) * 100m / f.Capacity, 2, MidpointRounding.AwayFromZero));
            })
            .OrderBy(line => line.FreeSeats)
            .ThenBy(line => line.FlightId, StringComparer.Ordinal)
            .ToImmutableArray();

        var flights = state.Flights.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var finance = state.Reservations
            .GroupBy(b => flights[b.FlightId].Destination)
            .Select(group => new FinanceLine(group.Key,
                group.Count(b => b.Status == ReservationStatus.Confirmed),
                group.Count(b => b.Status == ReservationStatus.Cancelled),
                group.Sum(b => b.TotalUsd), group.Sum(b => b.RefundUsd),
                group.Sum(b => b.TotalUsd - b.RefundUsd)))
            .OrderByDescending(line => line.RetainedUsd)
            .ThenBy(line => line.Destination.ToString(), StringComparer.Ordinal)
            .ToImmutableArray();
        return new AgencyReportSet(occupancy, finance);
    }
}
