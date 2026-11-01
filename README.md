# ASTRA — Booking Quote Pipeline / Assignments 5–8

This is the same **ASTRA Space Travel Agency** project used in Assignments 5–7.
It reads booking requests, validates the entire batch, selects available trips,
calculates quotes using a supplied policy, and returns sorted quotes and a total.
It also looks up a booking by Id. It calculates prices in memory; it does not
reserve seats, take payments, or write booking data.

ASTRA processes JSON booking requests using a validated immutable data pipeline,
explicit `Result<T>` failures, configurable pricing and two separate LINQ reports.
Assignment 8 extends this established project rather than introducing a new one.
The system still does not reserve seats or take payments.

## Build, run, and test

Install a **.NET 10 SDK**, not only the Runtime.
`global.json` requests SDK **10.0.112** with `rollForward: latestFeature`, allowing
a compatible newer .NET 10 feature band. Preview SDKs are disabled.
SDK: [Download .NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
The application uses the standard library. Existing test packages are unchanged:
xUnit **2.9.3**, Microsoft.NET.Test.Sdk **17.13.0**, and
xunit.runner.visualstudio **3.1.1**. The first restore/build requires NuGet access
or a local cache containing these packages.

Working directory for all commands: **the extracted archive's root**, containing
`Astra.slnx`, `global.json`, and this README. No machine-specific PATH is required
when the SDK is installed normally.

```sh
dotnet --version
dotnet build Astra.slnx
dotnet run --project Astra.App -- samples/bookings.json both 2030-06-15
dotnet run --project Astra.App -- find samples/bookings.json A
dotnet run --project Astra.App -- demo
dotnet test Astra.slnx
```

`standard`, `group`, and `both` select pricing policies. The review date is an
explicit input, so the result does not depend on the computer's current date.
Run `dotnet run --project Astra.App -- --help` to see all commands.

## Input, domain rules, and expected success

Input is a JSON array of objects with six required, non-null fields. Property
names are case-sensitive; enum string values are case-insensitive. Unknown or
duplicate properties, numeric enum values, invalid types, and invalid dates are
rejected.

| Field | Rule |
|---|---|
| `Id` | 1–40 visible characters after Trim; no control/format/line-separator/paragraph-separator characters; unique after Trim, using case-sensitive Ordinal comparison |
| `Destination` | `moon`, `mars`, `europa` |
| `Passengers` | Integer from 1 to 6 inclusive |
| `TravelClass` | `explorer`, `premium`, `first` |
| `Departure` | ISO date `yyyy-MM-dd`, matching the mission's schedule |
| `IsCancelled` | Boolean; missing is distinct from `false` |

| Mission | Base fare per passenger | Departure | Availability |
|---|---:|---|---|
| Moon | 49,000 USD | 2030-06-15 | Open |
| Mars | 2,400,000 USD | 2030-09-10 | Limited; remaining seats are not tracked |
| Europa | 8,700,000 USD | 2031-03-20 | Planned / preview; excluded from quotes |

Class multipliers: Explorer ×1, Premium ×1.35, First ×1.80.
`Standard` is base fare × passengers × class multiplier. `GroupDiscount` applies
an additional 10% discount for 4–6 passengers. All calculations use `decimal`;
each quote is rounded to cents using `AwayFromZero`, and the total adds the
rounded quotes.

The original pure stages are `SelectBookable → CalculateQuotes → Summarize`.
Cancelled bookings, preview destinations, and departures before `asOf` are
excluded. A departure on `asOf` is included. A valid request filtered out by
these rules is a successful selection decision. Quotes are sorted by ascending
price, then by Id using Ordinal comparison. `[]` is a valid batch and succeeds
with 0 quotes and a total of 0.

For `samples/bookings.json` at `asOf = 2030-06-15`:

| Id | Request | Standard, USD | Group, USD |
|---|---|---:|---:|
| A | Moon, 1, Explorer | 49,000 | 49,000 |
| B | Moon, 4, Premium | 264,600 | 238,140 |
| C | Mars, 6, First | 25,920,000 | 23,328,000 |
| D | Europa, Planned | excluded | excluded |
| E | Moon, cancelled | excluded | excluded |
| F | Mars, 3, Explorer | 7,200,000 | 7,200,000 |
| **Total** | **4 quotes, ordered A, B, F, C** | **33,433,600** | **30,815,140** |

At `2030-06-16`, F and C remain, with totals 33,120,000 and 30,528,000 USD.
At `2032-01-01`, both totals are 0.


## Task 1 — Custom extension methods

`Upcoming(this IEnumerable<BookingRequest>, DateOnly)` filters cancelled or prior
bookings without changing the collection. `WithContext<T>(this Result<T>, string)`
preserves successes and adds input context to failures. Each extension fits its
receiver type: booking sequences have temporal filtering; Result values carry
boundary error context. Both are used in application workflows and unit tests.

## Task 2 — LINQ reports

- **Quote list**: for sales staff; uses validated JSON requests and a selected
  pricing policy, excludes cancelled/past/unbookable missions, and produces
  deterministic Id/destination/class/passenger/price entries sorted by price then Id.
  For the sample date and `group10` policy, the accepted total is $30,815,140.
- **Demand by destination**: for operations; includes active/upcoming bookings
  including planned-preview trips, and groups by destination to show booking count,
  passenger total, forecast revenue. It sorts by revenue descending then destination
  name. For the same sample/policy, total $39,515,140.

`Where`, `Select`, ordering, `ThenBy`, `GroupBy`, `Count`, and `Sum` are used across
these reports. An empty or no-match set produces zero totals, and invalid records
fail before reports are printed. Query logic remains separate from console/CSV I/O.

## Task 3 — Currying

`Pricing.CreatePolicy` turns a `PricingConfig` into `Func<BookingRequest, decimal>`.
`Group10` (4+ passengers, 10%) and `Family5` (2+ passengers, 5%) are partially
applied functions on the same dataset. They produce distinct totals (30,815,140
and 31,764,370 USD) with no mutable global policy switching.

## Task 4 — Deferred execution

`BookingQuotes.PriceLazily` uses `yield return` to calculate project quotes only
during enumeration. Test counters confirm no work at enumerable construction,
work during iteration, recomputation on another enumeration, and stable reuse
after `ToImmutableArray`.

## Task 5 — Resource disposal

JSON is read using `File.ReadAllText` (automatic file-handle ownership).
CSV export uses `FileStream` and `StreamWriter` inside `using` scopes and an
atomic-style temp-file-then-move operation. Success and failure cleanup are tested.

## Task 6 — Memory retention

The project creates immutable snapshots for safe repeated reports. Deferred
iterators temporarily retain source/delegate references. The two configured
pricing delegates retain only small immutable configurations. No cache or
long-lived event subscriptions exist. Snapshotting uses O(n) memory but avoids
repeat reads and calculations. See README's resource ownership note.

## Task 7 — C library

A compiled Linux x86-64 native library exposes
`int astra_group_eligible(int passengers, int minimum)` with 32-bit integer ABI.
Both inputs must be 1–6. The result is 1 when passengers >= minimum, 0 otherwise,
and -1 for invalid data. Check: `(4,4) = 1`, `(3,4) = 0`.

## Task 8 — Calling C with P/Invoke

C# uses `DllImport` with explicit Cdecl signature. `NativeGroupEligibility`
validates parameters and returns Result failures for load/binding faults or bad
native status. `NativeReportCalculation` invokes the adapter for each **accepted**
quote. On the sample data it finds **2 group-eligible accepted bookings**, which is
shown in the normal CLI report. The report command produces no partial output or
CSV file if native integration fails.

## Task 9 — Build instructions and tests

Source/header, Linux shell build and Windows PowerShell build are included.
Linux GCC 14.2.0 C11 was used to produce `libastra_native.so`; the app and test
projects copy it beside the managed assemblies. Test code checks the actual native
result, adapter validation, injected loader failure, and full CLI integration.
The controlled failure avoids the unreliable approach of deleting a library
already loaded in the process. The C function uses no native allocation.

## Team and Contributions

**Group:** IT-2512  
**Team members:** Abdykhamit Beknur and Zhumazhan Miras.

The contributions for Assignment 8 were divided as follows:

| Team member | Assigned tasks | Implemented logic and responsibilities | Tests and documentation to review and defend |
|---|---|---|---|
| **Abdykhamit Beknur** | **Tasks 1–5** | Implemented the two custom extension methods (`Upcoming`, `WithContext`); built the LINQ quote-list and demand-by-destination reports; added configurable pricing via currying and partial application (`Group10`, `Family5`); implemented deferred processing with `yield return` and explicit materialisation; ensured safe file resource ownership and disposal. | Extension behaviour, report filtering/order/grouping and edge cases, configured-function outcomes, lazy execution/materialisation, and file cleanup tests; documentation of Tasks 1–5. |
| **Zhumazhan Miras** | **Tasks 6–9** | Documented retention/allocation and snapshot trade-offs; developed the native C function `astra_group_eligible` with its header and build scripts; implemented C# P/Invoke binding, Result-based native adapter and actual report integration; added native integration/error-path tests and reproducible build instructions. | Native-call and adapter-failure tests, integrated workflow checks, platform/build instructions, resource-retention documentation, and documentation of Tasks 6–9. |

Each team member is responsible for explaining, demonstrating and defending the tasks attributed to them, including the corresponding automated tests and a small live change during the defense.

## Testing status and demonstration

The native shared library was compiled on Linux x86-64 and checked for exported
functionality. **The .NET 10 SDK is unavailable in this environment**; therefore
managed build and xUnit execution remain *unverified*. Existing tests and new tests
are present, but actual passing-test counts must be obtained on a .NET 10 machine.

```sh
sh Astra.App/native/build-linux.sh
dotnet build Astra.slnx
dotnet test Astra.slnx
dotnet run --project Astra.App -- report samples/bookings.json 2030-06-15 group10 output.csv
dotnet run --project Astra.App -- report samples/malformed.json 2030-06-15 group10
```

The normal command should show both reports plus `Native C: 2 accepted booking(s)
have at least 4 passengers.` The malformed-input command should exit with a
clear failure and no report output.


