# ASTRA - Space Travel Agency final project

A C# console application for a two-person functional-programming final project.
It quotes, confirms, cancels and transfers reservations using immutable domain
snapshots, explicit Result failures, validated factories and configurable policies.
The Assignment 5-8 quote pipeline remains available.

Team: **Zhumazhan Miras (mmionya)** and **Abdykhamit Beknur (ADSKI-KIADS)**, IT-2512.
Repository: https://github.com/mmionya/assignment-6-ifp
Submission branch: `main`.
Source revision: see `SOURCE_VERSION.txt` in a packaged delivery, or `git log -1`
in the checkout. The package script records the exact source commit; do not submit
an archive while the checkout has uncommitted changes.

## Quick start

Supported/verified platform: Windows x64, .NET SDK 10.0.403. The repository's
`global.json` requests 10.0.112 and allows later .NET 10 feature bands. A runtime
alone is insufficient. Linux x64 native source/binary and build script are retained;
this final version has not been executed on Linux. NuGet access is needed for the
first test restore; `NuGet.Config` explicitly configures nuget.org.

```powershell
dotnet build Astra.slnx
dotnet test Astra.slnx
Copy-Item samples/agency.json demo-state.json
dotnet run --project Astra.App -- agency flights demo-state.json 2030-06-01
dotnet run --project Astra.App -- agency quote demo-state.json MOON-1 4 premium group10 2030-06-01
dotnet run --project Astra.App -- agency reserve demo-state.json NEW MOON-1 4 premium group10 2030-06-01
dotnet run --project Astra.App -- agency transfer demo-state.json NEW MOON-2 premium group10 2030-06-02
dotnet run --project Astra.App -- agency cancel demo-state.json NEW 2030-06-08
dotnet run --project Astra.App -- agency reports demo-state.json 2030-06-08 demo-report.csv
```

Use `cp` instead of `Copy-Item` in a Linux shell. All paths are explicit and relative
to the current directory. `agency help` lists commands. Dates are explicit business
inputs, never an implicit system clock. Samples use fictional 2030/2031 departures.

Expected example: quote/reserve **238140.00 USD**; transfer **252720.00 USD** with
**14580.00 USD** due; cancellation refund **252720.00 USD**. These are contractual
calculations; the application does not collect or transfer money.

## Four workflows and rules

| Workflow | Checks | Success |
|---|---|---|
| Quote | Valid passengers/class, existing open future flight, enough seats, valid policy result | Rounded price; no file change |
| Reserve | Quote prerequisites and unused booking ID | Confirmed reservation, occupied seats, persisted snapshot |
| Cancel | Found and confirmed; date on/after booking and before departure | Cancelled record, refund, released seats |
| Transfer | Active reservation; different future open flight of same destination; enough seats | New flight/class/price and balance; old seats released |

Passengers: 1-6 per reservation. Flight capacity: 1-10000. IDs: trimmed, case-sensitive,
1-40 visible characters; cancelled booking IDs cannot be reused. Monetary values use
`decimal`, cents, rounding AwayFromZero. Base fares are bounded to prevent overflow.

Explorer multiplier 1, Premium 1.35, First 1.8. Standard has no discount; Group10
reduces the price by 10% for 4+ passengers; Family5 reduces it by 5% for 2+ passengers.
The same Moon/4/Premium input costs 264600, 238140 and 251370 USD respectively.

Cancellation at least 7 days before departure refunds 100%; 1-6 days before departure
refunds 50%. No change is allowed on departure day. The refund uses the stored contract,
not a freshly computed tariff. Transfer preserves passenger count, ID and booking date;
it allows a new class and computes new price minus old price. A failed operation never
publishes a new snapshot or writes a partially updated state.

## Domain and architecture

- `Astra.Core/Reservations`: validated Flight and Reservation, immutable ReservationState,
  pricing, confirmation, cancellation, transfer, pure occupancy/finance reports.
- `Astra.Core`: preserved Result/Option, QuoteState cases, old quote pipeline, custom
  extension methods, curried configuration, lazy pricing and legacy reports.
- `Astra.App`: command parsing, console output, strict JSON DTOs, file locks/writes,
  native adapter and CSV export. These side effects stay outside the domain.
- `Astra.Tests`: xUnit assertions for pure rules, workflows, I/O and native integration.

```text
CLI -> validated arguments -> load JSON -> validate rows and cross-record state
    -> pure workflow -> new immutable snapshot -> save -> print success
Failure at any dependent stage skips later stages and reports a clear error.
```

Flight/Reservation constructors are private and properties are getter-only. The state
factory checks duplicate IDs, references, booking/cancellation dates and capacity.
Free seats are derived from confirmed reservations, avoiding a separate mutable counter.
The legacy quote process has Pending/Ready/Completed cases with different payloads;
pattern matching rejects calculation before preparation.

## Reports

`agency reports` uses the current persisted snapshot:

1. **Flight availability:** upcoming open flights, capacity, occupied/free seats and
   percentage; ordered by free seats then ordinal flight ID. Full flights remain visible.
2. **Contract value by destination:** confirmed/cancelled counts, stored contract totals,
   refunds and retained value; GroupBy/Count/Sum, sorted by retained value then destination.
   This is not a payment ledger or historical accounting report.

The sample has 11860000.00 USD retained contract value and one confirmed 4+ passenger
booking. Empty data/no upcoming flights produce valid empty reports and zero totals.
C `astra_group_eligible` supplies the count of confirmed group-eligible bookings in the
normal agency report. A native failure stops CSV export and printing of partial reports.

Legacy quote list and demand forecast are still available:

```powershell
dotnet run --project Astra.App -- samples/bookings.json both 2030-06-15
dotnet run --project Astra.App -- report samples/bookings.json 2030-06-15 group10 legacy.csv
dotnet run --project Astra.App -- find samples/bookings.json A
dotnet run --project Astra.App -- demo
```

The old sample/format is a batch of pricing requests; `agency.json` is the new persisted
reservation state. They are intentionally separate formats, not interchangeable files.

## Requirement map

| Technique | Source / evidence |
|---|---|
| Three named stages | ReservationBooking.ValidateRequest -> CheckAvailability -> CalculateQuote; old SelectBookable -> CalculateQuotes -> Summarize |
| Higher-order rule | Quote/Reserve/Transfer invoke a supplied pricing delegate |
| Currying / partial application | ReservationPricing.CreatePolicy; Group10 and Family5; existing Pricing.CreatePolicy |
| Result, Map, Bind | Reserve/Transfer chains and ReservationStore.Update; short-circuit tests |
| Explicit absence | ReservationState.FindFlight/FindReservation; BookingLookup.Find |
| Factories and safe ordering | Flight/Reservation/ReservationState.Create; QuoteState and QuoteWorkflow |
| Pattern matching | Quote validation, cancellation state checks, Option and QuoteState cases |
| Two extensions | BookingExtensions.Upcoming and WithContext, used by legacy report/input workflows |
| LINQ reports | AgencyReports.Build and BookingReports; filtering/projection/grouping/sum/tie-breakers |
| Lazy processing | BookingQuotes.PriceLazily uses yield; BookingReports materialises immutable arrays; deferred-execution tests |
| Resource ownership | ReservationStore.Update owns lock; ReportFiles.Export owns temporary file/streams; failure-disposal tests |
| Native C | Astra.App/native/astra_native.c, NativeGroupEligibility, normal reports and real-call tests |

## Persistence and resource ownership

`agency.json` contains `Version: 1`, `Flights` and `Reservations`. Required fields,
unknown/duplicate properties, enum values and business invariants are validated.
CancelledOn is null for active reservations; RefundUsd is explicitly present, including
zero. Malformed/missing files return failures and do not create a successful result.

A mutation holds an exclusive `<state>.lock` handle across load, business operation and
save. The empty lock file remains to avoid deletion races; it contains no personal data.
Concurrent ASTRA updates fail clearly instead of silently overwriting one another. Direct
external edits that ignore the lock are outside this guarantee.

Writes use a unique exclusive temporary file beside the destination. Streams are disposed
before replacement. Failures clean the owned temporary file and preserve the old target.
Report export rejects the input path, Windows case aliases and symbolic-link/junction
paths. Power-loss durability and externally managed hard-link aliases are not guaranteed.

Immutable snapshots use O(n) memory. There is no unbounded cache, event subscription or
native allocation. Lazy iterators retain their source/delegate until iteration completes;
materialised arrays allow repeated use without repeated business side effects.

## Native build and loading

The included `Astra.App/native/runtimes/win-x64/native/astra_native.dll` is copied beside
managed binaries on Windows. The real Windows native call was checked. To rebuild, use
an x64 GCC toolchain (for example MinGW-w64) with `gcc` on PATH:

```powershell
powershell -ExecutionPolicy Bypass -File Astra.App/native/build-windows.ps1
dotnet build Astra.slnx
```

Linux x64, GCC with C11 support:

```sh
sh Astra.App/native/build-linux.sh
dotnet build Astra.slnx
dotnet test Astra.slnx
```

The C ABI is `int astra_group_eligible(int passengers, int minimum)`, Cdecl, 32-bit ints,
returning 1/0 or -1 for invalid input. The adapter converts missing/bad binaries or symbols
to Result failures; there is no silent managed fallback. Runtime architecture must match
x64 binaries. This final delivery declares Windows x64; macOS is not verified/supported.

## Tests, ownership and submission

See `VERIFICATION.txt` for the measured final run, `docs/TEAM_PLAN.md` for ownership and
`docs/DEFENSE.md` for the rehearsal. Both students have meaningful domain logic and tests
assigned. Earlier Assignment 5-8 notes are preserved as historical documentation only.

Each team member is responsible for reviewing their assigned modules, understanding
the overall application, and accurately describing their own contribution.

Group and individual PDF review drafts are generated by `scripts/make_reports.py` into
`output/pdf`. Personal reflection sections require the student's own truthful wording.
No slide deck is required. Build a source archive with `scripts/package.py` after committing;
the package includes source, native materials, sample data and reports, excludes .git,
bin/obj/caches/locks, and records the source revision. It does not upload to Moodle.

Known limits: console UI; no authentication, bank payments, database, traveler identity,
flight editing UI, historical booking event log or production deployment. Dates are explicit
inputs for deterministic demonstrations, not an authenticated transaction clock. The four
workflows and current-state reports are implemented; these limits are not promised features.
