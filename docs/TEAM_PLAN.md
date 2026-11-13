# ASTRA final: Miras and Beknur

Group: IT-2512. Team: Zhumazhan Miras and Abdykhamit Beknur.

The owners below are responsible for integration, review and defense of their assigned
modules. Each student must understand the overall application and accurately describe
their own work in the individual contribution report.

| Owner | Implementation and review scope | Source evidence | Tests |
|---|---|---|---|
| Beknur / ADSKI-KIADS | Validated flight and reservation models, immutable state, price policies, quote pipeline and reservation confirmation | `Astra.Core/Reservations/Flight.cs`, `Reservation.cs`, `ReservationState.cs`, `ReservationPricing.cs`, `ReservationBooking.cs` | `ReservationBookingTests.cs` |
| Beknur / ADSKI-KIADS | Flight occupancy and contract-value reports, deterministic sorting and grouping | `Astra.Core/Reservations/AgencyReports.cs` | Report assertions in `AgencyWorkflowTests.cs` |
| Miras / mmionya | Cancellation, deadline/refund rules, transfer and atomic state replacement | `Astra.Core/Reservations/ReservationChanges.cs` | `ReservationChangesTests.cs` |
| Miras / mmionya | Strict JSON persistence, safe writes, CLI, file-lock ownership, export fixes and native failure integration | `Astra.App/ReservationStore.cs`, `AgencyCommand.cs`, `ReportFiles.cs`, `ReportCommand.cs` | `AgencyWorkflowTests.cs`, `ExportSafetyTests.cs` |
| Both | Integration review, reproducible setup, group report, defense rehearsal | `README.md`, `docs/DEFENSE.md` | Run the full suite and each demonstration |

## Four business workflows

1. Quote: validate request, check future/bookable flight and capacity, apply policy.
2. Reserve: quote, reject reused booking ID, create a confirmed reservation, save.
3. Cancel: reject missing/cancelled/departed reservation; refund 100% at least seven
   days before departure, otherwise 50%; release seats and save.
4. Transfer: require an active reservation, different flight with the same destination,
   future departure and available seats; recalculate the contract and replace the booking.
   An error preserves the old booking and file. Positive balance means an amount due;
   negative balance means a refund. No real payment is made.

## Review before submission

- Beknur: trace `ValidateRequest -> CheckAvailability -> CalculateQuote`, explain the
  delegate and the difference between Group10 and Family5; show factory rejection and
  the last-seat boundary. Explain why changing an input list does not change a snapshot.
- Miras: trace `RequireActive -> RequireTarget -> Quote -> Replace`, demonstrate failed
  transfer preserving state; explain Map/Bind, the JSON boundary and lock/write ownership.
- Both: explain legacy QuoteState cases, Option lookup, yield return/materialisation,
  native P/Invoke and actual native failure; run the complete test suite.
- Each student: rewrite the individual report in their own words, identify actual review
  or changes, tests they personally worked on and a technical difficulty they resolved.
- Record one source revision in both submission archives. Upload each student's own
  contribution report. Exclude bin, obj, IDE caches, locks and credentials.

The technical reports in `reports/` and `output/pdf` describe the assigned scope,
implementation and measured tests. Each student should review the text and adapt their
individual report to accurately reflect their own contribution before submission.
