# Defense rehearsal

Run from the repository root. Work with a copy of the sample state.

For a repeatable demonstration, run `python scripts/demo.py`. It builds Release,
creates a new `output/demo-*` directory, checks that the rejected transfer preserves
the file, and saves the final state, CSV and console transcript. It never edits the sample.

```powershell
Copy-Item samples/agency.json demo-state.json
dotnet run --project Astra.App -- agency quote demo-state.json MOON-1 4 premium group10 2030-06-01
dotnet run --project Astra.App -- agency reserve demo-state.json NEW MOON-1 4 premium group10 2030-06-01
dotnet run --project Astra.App -- agency transfer demo-state.json NEW MOON-FULL premium group10 2030-06-02
dotnet run --project Astra.App -- agency find demo-state.json NEW
dotnet run --project Astra.App -- agency transfer demo-state.json NEW MOON-2 premium group10 2030-06-02
dotnet run --project Astra.App -- agency cancel demo-state.json NEW 2030-06-08
dotnet run --project Astra.App -- agency reports demo-state.json 2030-06-08 demo-report.csv
dotnet test Astra.slnx
```

Expected sequence: quote/reserve 238140.00 USD; full-flight transfer fails and NEW
still references MOON-1; valid transfer costs 252720.00, balance 14580.00; cancellation
refunds 252720.00. The original `samples/agency.json` remains unchanged.

## Beknur: explain and demonstrate

- Private constructors and getter-only properties prevent an invalid `with` edit.
- State owns immutable snapshots; available seats are calculated from active bookings.
- Quote has three named stages, and failed prerequisites prevent policy invocation.
- Standard, Group10 and Family5 are pure functions. The configuration is captured once.
- Reports group real state and sort tied values deterministically.
- Small live change: add a 3+ passenger 7% policy through `PricingConfig.Create(3, 7)`;
  first add an assertion for 2 versus 3 passengers, then expose the policy in the CLI.

## Miras: explain and demonstrate

- Cancel uses the agreed stored price, not today's pricing policy.
- A transfer constructs a replacement snapshot only after every check passes.
- `Map` transforms a success; `Bind` composes a step which can fail. Neither runs on Failure.
- JSON DTOs are untrusted; factories validate every row and then cross-record invariants.
- A lock covers load, change and save. The writer owns a unique temporary file, disposes
  streams, then replaces the destination. Failure leaves the previous state intact.
- Small live change: require cancellation at least two days before departure. Add
  boundary assertions for one/two days, change the rule, and run the targeted tests.

## Shared functional examples

- `Option.Some(false)` is a present value; `Option.None` is absence.
- `QuoteState.Pending` cannot be calculated until preparation succeeds. The completed
  state carries its result and repeated calculation does not rerun pricing.
- `BookingQuotes.PriceLazily` does no pricing before enumeration. Reports materialise
  an immutable array to reuse a snapshot. Production policies have no side effects.
- C `astra_group_eligible` returns 1/0/-1. `DllImport` declares Cdecl and 32-bit integers.
  The adapter maps loader/binding faults to Result. The real missing-DLL test uses a
  fresh process, avoiding a DLL already loaded in the test process.

The final defense and personal report must reflect each student's actual understanding
and contribution.
