using Astra.Core;

namespace Astra.App;

internal static class TemporalDemo
{
    public static int Run()
    {
        var asOf = new DateOnly(2030, 6, 15);
        var moon = new RawBooking("MOON-DEMO", Destination.Moon, 1,
            TravelClass.Explorer, asOf, false);

        // Count calls here so the demo can show whether a calculation ran again.
        var policyCalls = 0;
        decimal CountedStandard(BookingRequest booking)
        {
            policyCalls++;
            return BookingPipeline.Standard(booking);
        }

        Console.WriteLine("ASTRA: handling temporal coupling in quote calculation");
        Console.WriteLine("Review date: 2030-06-15; policy: standard; no booking is submitted or saved.");

        Console.WriteLine("\n1. Missing prerequisite: calculate a Pending state");
        QuoteState current = new QuoteState.Pending([moon], asOf, CountedStandard);
        var beforeMissing = current;
        var acceptedMissing = CommitAndPrint(QuoteWorkflow.Calculate(current), ref current);
        var missingUnchanged = ReferenceEquals(beforeMissing, current);
        Console.WriteLine($"Current state: {StateName(current)}; unchanged: {missingUnchanged}; policy calls: {policyCalls}");
        var expected = !acceptedMissing && missingUnchanged && policyCalls == 0;

        Console.WriteLine("\n2. Factory rejection: valid first row, zero passengers in the second row");
        RawBooking?[] source = [moon, moon with { Id = "INVALID-DEMO", Passengers = 0 }];
        var sourceBefore = source.ToArray();
        var invalidPending = new QuoteState.Pending(source, asOf, CountedStandard);
        QuoteState invalidCurrent = invalidPending;
        var invalidResult = QuoteWorkflow.Prepare(invalidPending);
        var acceptedInvalid = CommitAndPrint(invalidResult, ref invalidCurrent);
        var invalidUnchanged = ReferenceEquals(invalidPending, invalidCurrent);
        var sourceUnchanged = source.SequenceEqual(sourceBefore);
        Console.WriteLine($"Current state: {StateName(invalidCurrent)}; unchanged: {invalidUnchanged}; source unchanged: {sourceUnchanged}");
        Console.WriteLine($"Complete validated batch returned: {invalidResult.IsSuccess}; policy calls: {policyCalls}");
        expected &= !acceptedInvalid && invalidUnchanged && sourceUnchanged && policyCalls == 0;

        Console.WriteLine("\n3. Normal order: Pending -> Ready -> Completed");
        var pending = new QuoteState.Pending([moon], asOf, CountedStandard);
        QuoteState normalCurrent = pending;
        Console.WriteLine($"Current state: {StateName(normalCurrent)}");
        var prepared = CommitAndPrint(QuoteWorkflow.Prepare(pending), ref normalCurrent);
        expected &= prepared;
        if (prepared)
        {
            var calculated = CommitAndPrint(QuoteWorkflow.Calculate(normalCurrent), ref normalCurrent);
            expected &= calculated;
        }

        switch (normalCurrent)
        {
            case QuoteState.Completed completed:
                Console.WriteLine(FormattableString.Invariant(
                    $"Moon / Explorer / 1 passenger: {completed.Summary.TotalUsd:F2} USD; policy calls: {policyCalls}"));
                expected &= completed.Summary.Quotes.Length == 1
                    && completed.Summary.TotalUsd == 49_000m && policyCalls == 1;
                break;
            case QuoteState.Pending:
                Console.WriteLine("No summary: preparation did not complete.");
                expected = false;
                break;
            case QuoteState.Ready:
                Console.WriteLine("No summary: calculation did not complete.");
                expected = false;
                break;
            default:
                Console.WriteLine("Unknown quote state.");
                expected = false;
                break;
        }

        Console.WriteLine("\n4. Repeating calculation on Completed reuses the result");
        var beforeRepeat = normalCurrent;
        var callsBeforeRepeat = policyCalls;
        var acceptedRepeat = CommitAndPrint(QuoteWorkflow.Calculate(normalCurrent), ref normalCurrent);
        var sameObject = ReferenceEquals(beforeRepeat, normalCurrent);
        var policyNotRepeated = policyCalls == callsBeforeRepeat;
        Console.WriteLine($"Same completed object: {sameObject}; policy called again: {!policyNotRepeated}; total policy calls: {policyCalls}");
        expected &= acceptedRepeat && normalCurrent is QuoteState.Completed && sameObject && policyNotRepeated;

        Console.WriteLine(expected ? "\nDemo finished." : "\nDemo encountered an unexpected outcome.");
        return expected ? 0 : 1;
    }

    private static bool CommitAndPrint<TState>(Result<TState> result, ref QuoteState current)
        where TState : QuoteState
    {
        if (!result.IsSuccess)
        {
            Console.WriteLine($"Rejected: {result.Error}");
            return false;
        }

        // Replace the current state only when the operation succeeds.
        current = result.Value;
        Console.WriteLine($"Accepted: current state is {StateName(current)}");
        return true;
    }

    private static string StateName(QuoteState state) => state switch
    {
        QuoteState.Pending => "Pending",
        QuoteState.Ready => "Ready",
        QuoteState.Completed => "Completed",
        _ => "Unknown"
    };
}
