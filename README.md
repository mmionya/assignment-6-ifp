# ASTRA — Booking Quote Pipeline / Assignment 6

This module continues the **ASTRA Space Travel Agency** project from Assignment 5.
It validates a batch of booking requests, selects available trips, calculates
prices using the chosen policy, and returns sorted quotes with a total.
Its users are visitors comparing prices and the ASTRA operator.
The module calculates quotes; it does not reserve seats or process payments.

The mission catalog, prices, both policies, functional stages, JSON files, and
Assignment 5 tests are preserved. Assignment 6 adds a protected batch factory,
explicit states, and safe transitions. The same module will be retained for
Assignments 7–8 and the future ASTRA server. The original HTML/CSS website does
not call this C# module yet.

## Quick start

Install **.NET SDK 10.0.112**, not just the Runtime. `global.json` selects this
version and allows a newer patch within the same 10.0.1xx feature band.
SDK: [Download .NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
The application uses the standard library. Tests retain the existing packages:
xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.13.0, and xunit.runner.visualstudio 3.1.1.
The first build requires access to NuGet or a local cache containing these packages.

On the machine used to prepare this copy, the SDK is installed locally at
`~/.local/share/assignment-6-dotnet`. Add it to the current bash session's PATH
before running the commands below. On another computer, install the SDK normally.

```sh
export PATH="$HOME/.local/share/assignment-6-dotnet:$PATH"
```

Working directory: **the extracted archive's root**, containing `Astra.slnx` and
this README. The following commands work on Linux, macOS, and Windows:

```sh
dotnet --version
dotnet build Astra.slnx
dotnet run --project Astra.App -- samples/bookings.json both 2030-06-15
dotnet run --project Astra.App -- demo
dotnet test Astra.slnx
```

`both` displays both policies; `standard` and `group` are also supported.
The review date is an explicit input, so the program does not depend on the
computer's current date. For help, run `dotnet run --project Astra.App -- --help`.

## Task 1. The hidden dependency: before and after

In Assignment 5, `Input.Process` already performed validation in the correct order.
The weakness was in the core's public API: `BookingPipeline.Run` accepted an
ordinary `ImmutableArray<BookingRequest>`. External code could construct requests
manually, skip `Input.Validate`, and still receive a successful calculation.

**Unsafe example of the old API**, kept only in this documentation:

```csharp
// Assignment 5: this call compiled even though validation had not run.
var uncheckedRequests = ImmutableArray.Create(new BookingRequest(
    "BAD", Destination.Moon, 0, TravelClass.Explorer,
    new DateOnly(2030, 6, 15), false));
var summary = BookingPipeline.Run(
    uncheckedRequests, new DateOnly(2030, 6, 15), BookingPipeline.Standard);
// Quotes.Length == 1, TotalUsd == 0: a quote for ZERO passengers!
```

The prerequisite is a batch that satisfies every domain rule.
The hidden order was `Parse → Validate → Run`: validation had to precede
calculation. Skipping it could produce a zero or negative price, or an exception
for an unknown destination. Validating after calculation cannot undo a result
already returned to the caller. This unsafe call no longer compiles in A6.

**The safe application path** (`Astra.App/Input.cs`, `Process`):

```csharp
return Parse(json)
    .Bind(rows => QuoteWorkflow.Prepare(new QuoteState.Pending(rows, asOf, policy)))
    .Bind(ready => QuoteWorkflow.Calculate(ready))
    .Map(completed => completed.Summary);
```

`Prepare` calls the factory, and `Calculate` matches the explicit state.
An arbitrary array cannot be used to construct `Ready`. Calling `Calculate(pending)`
without `Prepare` returns a `Missing prerequisite` error without invoking the
pricing policy.

## Task 2. The validated factory

`Astra.Core/ValidatedBookingBatch.cs`, method `ValidatedBookingBatch.Create`:

1. Checks that the dataset, each record, and every required field are present.
2. Checks Id: 1–40 characters after Trim, no control characters, and uniqueness
   after Trim using case-sensitive comparison.
3. Checks that the destination and travel class exist in their enumerations.
4. Checks the passenger count: **1–6 inclusive**.
5. Checks that the departure date matches the chosen mission's schedule.

Success returns `Result<ValidatedBookingBatch>.Success` containing a usable batch.
Failure returns the first error with a specific reason and, for record errors,
the record number. The local builder never escapes, even when an invalid record
follows valid ones. No partial successful total is produced.
`Input.Validate` remains a thin adapter to this factory, so the rules are not duplicated.

Construction cannot bypass validation through the public API:

- `ValidatedBookingBatch` is a `sealed class` with a **private** constructor.
- `Bookings` has only a getter and contains an `ImmutableArray<BookingRequest>`.
- Elements are immutable records. `with` creates a separate element and cannot
  replace an element in the existing batch. The validated batch itself is not a record.
- `BookingPipeline.Run` requires a `ValidatedBookingBatch`. The individual stages
  `SelectBookable`, `CalculateQuotes`, and `Summarize` are now `internal`.
  `InternalsVisibleTo("Astra.Tests")` preserves direct testing of the original stages.
- The factory and `Pending` copy input collections. Replacing an element in the
  original array after validation cannot change the prepared state.

These examples intentionally do not compile and are **not included in source files**:

```csharp
new ValidatedBookingBatch(uncheckedRequests); // private constructor
BookingPipeline.Run(uncheckedRequests, asOf, policy); // requires ValidatedBookingBatch
new QuoteState.Ready(...); // internal constructor, unavailable to the application
```

## Tasks 3–4. Explicit states and pattern matching

`Astra.Core/QuoteState.cs` defines a closed hierarchy: a private base constructor
and three nested `sealed` cases. External code cannot add a fourth case.
This is a discriminated-union-style model with a separate payload for each case.

| State | Data | Meaning |
|---|---|---|
| `Pending` | Immutable snapshot of raw records, review date, pricing policy | Input is available, but its validity has not been established |
| `Ready` | `ValidatedBookingBatch`, date, policy | The entire batch passed validation and may be calculated |
| `Completed` | `QuoteSummary`: quotes and total | Calculation is complete; the result can be retrieved again |

`Ready` and `Completed` can only be constructed inside the core.
`QuoteWorkflow.Calculate` uses a switch expression in the actual application path:

```csharp
return state switch
{
    QuoteState.Pending => Result<QuoteState.Completed>.Failure(
        "Missing prerequisite: prepare and validate the batch before calculating quotes."),
    QuoteState.Ready ready => Result<QuoteState.Completed>.Success(new QuoteState.Completed(
        BookingPipeline.Run(ready.Batch, ready.AsOf, ready.PricePolicy))),
    QuoteState.Completed completed => Result<QuoteState.Completed>.Success(completed),
    _ => Result<QuoteState.Completed>.Failure("Unknown quote state.")
};
```

Every declared case is handled explicitly. The fallback rejects an unknown state.
Each payload is accessed through its matching pattern, without casts or
null-forgiving access that assumes the wrong case's data exists.

## Task 5. Safe transitions and repeated operations

```text
Pending -- Prepare / all rules pass --> Ready -- Calculate --> Completed
   |                                      |                       |
   +-- Calculate --> Failure              +-- pure calculation     +-- Calculate --> same Completed
   +-- Prepare / invalid input --> Failure
```

`Prepare` and `Calculate` leave the input object, collections, and global state
unchanged. They return a new value, which the application adopts only after
success. Validation does not reserve seats or write files. The module has no
database or external store that would require rollback.

Repeat rule: `Calculate(completed)` returns **the same `Completed` instance**
without invoking the policy again. Calling `Calculate(ready)` again with the
original immutable `Ready` repeats the pure calculation and produces equal values.
A different policy or review date is supplied through a new `Pending`; it does
not change an existing completed result.

The core does not read files, clocks, console input, or random values. I/O belongs
to `Astra.App`, in `Program.cs` and `TemporalDemo.cs`.
The supplied `Func<BookingRequest, decimal>` pricing policy must be pure and
return a nonnegative amount within the `decimal` range. The application policies,
`Standard` and `GroupDiscount`, follow this contract. They are formulas used
within calculation, not independent operations for accepting unchecked input.
Programming errors in a custom delegate are not disguised as input validation errors.

## Input and preserved ASTRA rules

Input is a JSON array of objects with six required, non-null fields.
Property names are case-sensitive; enum string values are case-insensitive.
Unknown or duplicate properties, numeric enum values, invalid types, and invalid
dates are rejected.

| Field | Rule |
|---|---|
| `Id` | 1–40 characters after Trim; no control characters; unique Id |
| `Destination` | `moon`, `mars`, `europa` |
| `Passengers` | Integer from 1 to 6 |
| `TravelClass` | `explorer`, `premium`, `first` |
| `Departure` | ISO date `yyyy-MM-dd`, matching the mission schedule |
| `IsCancelled` | Boolean; a missing field is distinct from `false` |

| Mission | Base fare per passenger | Departure | Availability |
|---|---:|---|---|
| Moon | 49,000 USD | 2030-06-15 | Open |
| Mars | 2,400,000 USD | 2030-09-10 | Limited; remaining seats are not tracked |
| Europa | 8,700,000 USD | 2031-03-20 | Planned / preview; excluded from calculation |

Class multipliers: Explorer ×1, Premium ×1.35, First ×1.80.
`Standard` is base fare × passengers × class multiplier.
`GroupDiscount` applies an additional 10% discount for 4–6 passengers.
All calculations use `decimal`. Each quote is rounded to two decimal places
using `AwayFromZero`, and the total is the sum of the rounded quotes.

After validation, the batch passes through the original three pure stages:
`SelectBookable → CalculateQuotes → Summarize`.
Cancelled requests, preview destinations, and departures before `asOf` are excluded.
A departure exactly on `asOf` is included. A valid request that is filtered out
is not an input error. Quotes are sorted by ascending price, then by Id using
`Ordinal` comparison. `[]` succeeds with a total of 0.

For `samples/bookings.json` with `asOf = 2030-06-15`:

| Id | Request | Standard, USD | Group, USD |
|---|---|---:|---:|
| A | Moon, 1, Explorer | 49,000 | 49,000 |
| B | Moon, 4, Premium | 264,600 | 238,140 |
| C | Mars, 6, First | 25,920,000 | 23,328,000 |
| D | Europa, Planned | excluded | excluded |
| E | Moon, cancelled | excluded | excluded |
| F | Mars, 3, Explorer | 7,200,000 | 7,200,000 |
| **Total** | **4 quotes, ordered A, B, F, C** | **33,433,600** | **30,815,140** |

At `asOf = 2030-06-16`, F and C remain, with totals of 33,120,000 and 30,528,000 USD.
At `asOf = 2032-01-01`, both totals are 0.

## Demonstrations

```sh
# A6: success, missing validation, invalid batch, and safe repetition
dotnet run --project Astra.App -- demo

# The complete Assignment 5 batch with both policies
dotnet run --project Astra.App -- samples/bookings.json both 2030-06-15

# Record 2 has Passengers=0; record 1 must not produce a partial result
dotnet run --project Astra.App -- samples/invalid.json standard 2030-06-15

# Other boundary and failure scenarios
dotnet run --project Astra.App -- samples/empty.json both 2030-06-15
dotnet run --project Astra.App -- samples/invalid-schedule.json standard 2030-06-15
dotnet run --project Astra.App -- samples/missing.json standard 2030-06-15
dotnet run --project Astra.App -- samples/malformed.json standard 2030-06-15
dotnet run --project Astra.App -- samples/no-such-file.json standard 2030-06-15
```

`demo` uses a separate Moon/1/Explorer example with a **49,000 USD** result.
It also demonstrates `Missing prerequisite` when calculating `Pending`, factory
rejection of zero passengers, and repetition without another policy invocation.
`invalid` reports `Record 2: Passengers must be between 1 and 6`.
`empty` succeeds with 0 quotes and a 0 total. `invalid-schedule` reports a departure
that does not match the schedule; `missing` reports required fields;
`malformed` reports `Invalid JSON`; a missing file reports `Cannot read input`.
Normal application errors go to stderr, and no partial successful total is printed.
Exit codes: **0** for success/help/a successful demo, **1** for input or file errors,
and **2** for invalid CLI arguments or date format.

## xUnit tests and assignment coverage

The automated tests run through **xUnit**. `[Fact]` declares a single scenario;
`[Theory]` with `[InlineData]` runs a scenario with several inputs. `Assert` checks
actual values against expected results. `dotnet test` reports failures and returns
a nonzero exit code if a test fails. The demo's console output does not replace tests.

The 19 Assignment 5 test methods are retained, producing 29 cases with theory inputs.
`Astra.Tests/TemporalCouplingTests.cs` adds 17 methods producing 20 cases.
Latest run, **18 October 2026**: **49 passed, 0 failed, 0 skipped**, using
.NET SDK **10.0.112** on Linux x64.

| Requirement | File / method | Expected assertion or evidence |
|---|---|---|
| Task 1: hidden prerequisite | This README, before-and-after section | An unchecked batch previously produced a zero-price quote; the API now rejects that input type |
| Task 2: factory | `ValidatedBookingBatch.Create` | Valid data creates a usable batch; every rule rejects invalid input with a reason |
| Task 3: distinct cases | `QuoteState.Pending`, `Ready`, `Completed` | All three cases are reachable and carry their own payload |
| Task 4: matching | `QuoteWorkflow.Calculate`, called by `Input.Process` | Pending → rejection, Ready → calculation, Completed → existing result |
| Task 5: safe operations | `Prepare`, `Calculate`, `Input.Process` | Calculation requires validation; repetition is safe; failures preserve state |
| FACTORY SUCCESS | Factory tests | Expected Id, mission, passenger count, and normalization |
| FACTORY REJECTION | Factory tests plus `RequiredFields`, `InvalidFields`, `PassengerBoundaries` | Reasons cover every rule; no partially valid batch escapes |
| DISTINCT CASES / NORMAL | Transition tests plus `NormalWorkflow`, `TwoPolicies` | Every branch and the exact prices and totals |
| MISSING PREREQUISITE | Pending calculation test | Rejection and zero policy invocations |
| REPEATED OPERATION | Completed repetition test | The same object and an unchanged invocation count |
| BOUNDARY VALUE | Passenger, Id, date, and discount boundaries | 1 and 6 are accepted; 0 and 7 are rejected; the exact departure date is included |
| STATE PRESERVATION | Snapshot and invalid-batch tests, `Purity` | Raw input, Pending, and the previous result remain unchanged |
| Preserved functionality | `BookingTests.cs` | 29 A5 cases covering input, prices, stages, purity, and Map/Bind |

New tests and their specific expectations:

| Method in `TemporalCouplingTests` | Expected result |
|---|---|
| `FactoryCreatesCompleteNormalizedValue` | Id is trimmed and all valid request data is retained |
| `MissingDatasetFailsAndEmptyDatasetProducesUsableBatch` | null is rejected; [] creates a usable batch with a 0 total |
| `FactoryRejectsNullRowAndEveryMissingFieldWithoutExposingValue` | A null record and each missing field produce a reason; Value remains inaccessible |
| `FactoryRejectsBlankAndControlCharacterIds` | All 4 blank/control-character Id cases are rejected |
| `FactoryEnforcesExactIdLengthAndPassengerLimits` | 40 characters and 1/6 passengers are accepted; 41 characters and 0/7 passengers are rejected |
| `FactoryChecksTrimmedIdsWithOrdinalCaseSensitiveUniqueness` | A and " A " are duplicates; A and a are distinct Ids |
| `FactoryRejectsEachUndefinedEnumBeforeConsultingSchedule` | Undefined Destination and TravelClass values produce a clear error |
| `FactoryRequiresTheChosenMissionsExactDeparture` | Each mission's exact date is accepted; the following day is rejected |
| `ValidatedCapabilityHasNoPublicConstructionOrMutationPath` | No public constructor or setter exists; Run requires the validated type |
| `StateCasesAreClosedAndPreparedStatesCannotBeConstructedPublicly` | Exactly 3 sealed cases exist; Ready/Completed cannot be constructed externally |
| `PendingAndValidatedBatchOwnSnapshotsOfMutableSource` | Editing the source list or a separate copy does not change the snapshots |
| `PrepareRetainsConfigurationWithoutInvokingPolicy` | Date and delegate are retained; preparation invokes the policy 0 times |
| `CalculatingPendingRejectsMissingPrerequisiteWithoutPolicyCalls` | Explicit rejection, 0 invocations, and unchanged original state |
| `ReadyCalculatesAnImmutableCompletedSummaryUsingItsStoredPolicy` | Prices are 12.35 and 24.69, total is 37.04, and the original batch is unchanged |
| `RepeatingCompletedReturnsSameInstanceAndNeverCallsPolicyAgain` | Two repetitions return the same result; the policy is invoked only once in total |
| `RepeatingReadyRecomputesDeterministicallyForAPurePolicy` | Two new results contain equal quotes and totals |
| `FailureAfterValidRowLeavesInputsAndPreviouslyCompletedResultUnchanged` | Record 2 fails; raw input, Pending, and the earlier Completed remain unchanged |

List all test cases with:

```sh
dotnet test Astra.slnx --list-tests
```

Actual build, test, and clean archive extraction results are recorded in
`VERIFICATION.txt` (18 October 2026, Linux x64, SDK 10.0.112).

## Team and agreed responsibilities

Group: **IT-2512**. Members: **Beknur Abdykhamit** and **Zhumazhan Miras**.
The owner of this submission copy is Zhumazhan Miras.

| Member | Agreed Assignment 6 responsibility | Tests and documentation |
|---|---|---|
| Beknur Abdykhamit | `ValidatedBookingBatch.Create`, protected construction, `Input.Validate` adapter | Factory success and all rejection reasons, boundaries, and prevention of partial batches; domain rules and Task 2 |
| Zhumazhan Miras | `QuoteState`, `QuoteWorkflow`, `Input.Process`, demo/CLI | Cases and matching, missing prerequisites, repetition, and state preservation; Tasks 1 and 3–5, run instructions |

This table records agreed responsibilities, not evidence of completed individual
contributions. The code was prepared with assistance from an AI assistant.
Before submission, each member must record the actual code changes, tests, and
documentation they personally reviewed, verified, and improved.

## Preparing for the defense

1. Compare the old and new `Run` calls. Explain why providing a `Validate` method
   alone does not prevent a caller from skipping it.
2. Show the factory's private constructor, rules, local builder, and immutable
   result. Explain why an invalid second record prevents the entire batch from escaping.
3. Open `QuoteState` and `QuoteWorkflow.Calculate`. Identify each state's payload
   and matching branch, and explain the private base constructor.
4. Run `demo`, the normal JSON input, and `invalid.json`, then run `dotnet test Astra.slnx`.
5. Explain why Completed is returned without recalculation, while the original
   Ready can be calculated again using a pure function. A policy change requires new state.
6. Live modification exercise: change `MaximumPassengers` from 6 to 5.
   Run the boundary tests and observe the failure of the old expectation for 6.
   Then update the rule's expected behavior and test: 5 is accepted, while 6 is
   rejected with a reason. Sample C also contains 6 passengers, so its data and
   expected totals must be updated if the new limit is retained.
   Keep the original limit of 6 in the baseline submission.

`Result<T>` from A5 is retained: `Map` transforms success, and `Bind` chains an
operation that returns a `Result`. Neither invokes its delegate after failure.
This type and the workflow states can be extended in Assignment 7 within the same project.

## Submission

Public repository: [mmionya/assignment-6-ifp](https://github.com/mmionya/assignment-6-ifp).

Submission file: `IT-2512_Zhumazhan_Miras_Assignment6.zip`.
The archive includes `.cs` sources, `.csproj` projects, the `Astra.slnx` solution,
`global.json`, `Directory.Build.props`, README, verification results, tests, and samples.
It excludes `bin/`, `obj/`, IDE caches, `.git/`, secrets, and the installed SDK.
To reproduce the result, extract the archive and run the commands at the start
of this README. The owner uploads the archive to Moodle. The Git repository
supports collaboration and later project stages; its link does not replace the
required ZIP submission.
