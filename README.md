# ASTRA — Booking Quote Pipeline / Assignment 7

This is the same **ASTRA Space Travel Agency** project used in Assignments 5–6.
It reads booking requests, validates the entire batch, selects available trips,
calculates quotes using a supplied policy, and returns sorted quotes and a total.
It also looks up a booking by Id. It calculates prices in memory; it does not
reserve seats, take payments, or write booking data.

Assignment 7 uses explicit success/failure and missing-value outcomes in these
actual workflows. The Assignment 5 pure pipeline and Assignment 6 validated
factory, immutable state snapshots, and safe transitions remain active. The
original HTML/CSS website does not call this C# module yet.

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

## Task 1: explicit domain outcomes

`Astra.Core/Result.cs` defines `Result<T>` with a private constructor.
`Success(value)` requires a non-null payload. `Failure(error)` requires a useful,
nonblank message and exposes no successful payload. `IsSuccess` identifies the
case; reading `Value` from a failure throws a programming-error exception. The
application uses `Match` to handle a final outcome without reading a failed value.
A caller cannot construct contradictory success/error states through this API.

Expected domain rejections return `Failure`; they do not throw and catch
exceptions as business control flow.

| Scenario | Expected success or useful domain failure |
|---|---|
| Valid supplied batch | 4 quotes; standard total 33,433,600 USD, group total 30,815,140 USD |
| Invalid passenger count (`samples/invalid.json`) | `Record 2: Passengers must be between 1 and 6.`; no quote for the preceding valid record escapes |
| Wrong departure (`samples/invalid-schedule.json`) | `Record 1: Departure must match the mission schedule.` |
| Duplicate Id (`samples/duplicate-id.json`) | `Record 2: Id must be unique after trimming (case-sensitive).` |
| Missing required field (`samples/missing.json`) | Record number plus the required field names; the batch is rejected |
| Required booking lookup with unknown Id | `Booking with Id 'MISSING' was not found.` |
| Calculate a `Pending` state directly (`demo`) | `Missing prerequisite: prepare and validate the batch before calculating quotes.` |

File-based commands add input-file context to parse/domain errors. The first
invalid record rejects the whole batch; a partial successful total is never
returned.

## Task 2: explicit missing values

The existing Task 2 implementation is preserved. `Astra.Core/Option.cs` defines
`Option<T>.Some` with a required payload and `Option<T>.None` without one.
`BookingLookup.Find` in `Astra.Core/BookLookup.cs` searches a validated batch by
trimmed, case-sensitive Id and returns one of these cases. It does not fabricate
a booking or treat `default(T)` as evidence that a booking exists.

`BookingLookup.Require` explicitly matches both cases. `Some` becomes
`Result<BookingRequest>.Success`; `None` becomes a meaningful failure naming the
requested Id. `Input.FindBooking` composes parsing, batch validation, and this
required lookup; the `find` CLI command uses the same path.

Absence is separate from data. `Some<int>(0)` and `Some<bool>(false)` represent
present primitive values. A real booking with `IsCancelled = false` is present,
and the enum value `Destination.Moon` is valid even though its numeric value is
0. In raw JSON input, nullable fields distinguish a missing passenger count
from `Passengers = 0`: the latter is present but violates the 1–6 rule.
A missing `IsCancelled` fails validation; `false` is valid. An empty validated
batch is a valid value, while a lookup in it returns `None`.

A validation `Result` answers whether input satisfies domain rules. A
`QuoteState` answers which workflow stage has been reached. An `Option` answers
whether a lookup found an item. These types describe different questions.

## Task 3: Map, Bind, and failure propagation

The normal quote workflow in `Astra.App/Input.cs` invokes both operations:

```csharp
return Parse(json)
    .Bind(rows => QuoteWorkflow.Prepare(new QuoteState.Pending(rows, asOf, policy)))
    .Bind(ready => QuoteWorkflow.Calculate(ready))
    .Map(completed => completed.Summary);
```

`Map` takes `T → U`: it transforms a successful value with a pure function and
wraps the result. Here it extracts the summary from `Completed`. `Bind` takes
`T → Result<U>`: it continues with an operation that can reject the input,
without producing a nested `Result<Result<U>>`. `Prepare` also uses Map to turn
a validated batch into `Ready`.

After failure, Map and Bind preserve the same error and invoke neither supplied
function. Consequently, bad JSON skips preparation and calculation; an invalid
batch skips pricing; a failed lookup produces no fabricated result. Tests use
counters/flags to prove non-execution, rather than inferring it from output.
`Result.Match` selects exactly one final success/failure handler.

`BookingFiles.LoadQuotes` composes file reading with `Input.Process`.
`BookingFiles.FindBooking` composes reading with `Input.FindBooking`.
`CommandLine.BuildReports` calculates every selected policy from one file
snapshot and accumulates immutable reports before printing any success.
`CommandLine.Run` uses a final Match to print the completed reports or one clear
error. The CLI does not access a success payload after failure.

## Tasks 4–5: exception boundaries and application integration

File I/O belongs to `Astra.App/BookingFiles.cs`, not the pure core.
`BookingFiles.Read` catches the relevant file/path exceptions (`IOException`,
`UnauthorizedAccessException`, `ArgumentException`, `NotSupportedException`) and
returns a failure naming the attempted file. A missing file is detected as an
I/O failure. `Input.Parse` catches `JsonException` and returns `Invalid JSON`
with parser context; the file wrapper adds the input path.

An invalid passenger count, duplicate Id, schedule mismatch, and missing booking
are expected domain failures. A missing/unreadable file or malformed JSON is an
operational/input-boundary problem. Both are explicit failures with useful
context, while their causes and implementation locations remain distinct.
There is no silent fallback, empty catch, or failure reported as success.

Parsing and validation use local values. The factory's local builder escapes
only when every row succeeds. `Pending` and the validated batch own immutable
snapshots; transitions return new state and do not mutate input or a previously
completed result. This program writes no booking files or persistent state, so a
failed read, parse, or validation cannot leave a partial update. Tests also
compare original file contents and an earlier completed summary after failures.

Unexpected programming errors in a custom pricing delegate are not converted
into an ordinary domain success or silently hidden. Policies supplied to the
core must be pure and return a nonnegative price within `decimal` range.

## Demonstrations and expected outcomes

```sh
# Normal quotes: 4 quotes and totals 33433600.00 / 30815140.00 USD
dotnet run --project Astra.App -- samples/bookings.json both 2030-06-15

# Three distinct domain failures
dotnet run --project Astra.App -- samples/invalid.json standard 2030-06-15
dotnet run --project Astra.App -- samples/invalid-schedule.json standard 2030-06-15
dotnet run --project Astra.App -- samples/duplicate-id.json standard 2030-06-15

# Present lookup: A, Moon, 1 passenger, Cancelled: False
dotnet run --project Astra.App -- find samples/bookings.json A

# Absent required lookup: a clear failure naming MISSING
dotnet run --project Astra.App -- find samples/bookings.json MISSING

# File failures: Cannot read input / Invalid JSON, with the file path
dotnet run --project Astra.App -- samples/no-such-file.json standard 2030-06-15
dotnet run --project Astra.App -- samples/malformed.json standard 2030-06-15

# Valid empty data, missing fields, and earlier safe state transitions
dotnet run --project Astra.App -- samples/empty.json both 2030-06-15
dotnet run --project Astra.App -- samples/missing.json standard 2030-06-15
dotnet run --project Astra.App -- demo
```

`no-such-file.json` is intentionally absent. `empty.json` succeeds with 0 quotes
and 0 USD. `missing.json` fails required-field validation. The `demo` retains
A6's separate Moon/1/Explorer example (49,000 USD), rejected `Pending`
calculation, zero-passenger rejection, and safe repeated calculation.
Normal failures go to stderr, with no partial total. Exit codes: **0** for
success/help/successful demo, **1** for input/domain/file failures, and **2** for
invalid CLI arguments or date format.

## Preserved Assignment 5–6 design

Previously, `BookingPipeline.Run` accepted an arbitrary array: an external caller
could skip validation and quote zero passengers. It now requires a
`ValidatedBookingBatch`, whose sealed class has a private constructor, a
getter-only immutable collection, and immutable elements. Only `Create` can
produce it after checking every rule. The individual pipeline stages are
internal; `InternalsVisibleTo("Astra.Tests")` keeps their direct tests.
`Input.Validate` is a thin factory adapter, so validation rules are not duplicated.

`QuoteState` is a closed hierarchy (private base constructor, three sealed cases):

| State | Data | Allowed behavior |
|---|---|---|
| `Pending` | Snapshot of raw records, review date, policy | Prepare/validate; direct calculation fails |
| `Ready` | Validated batch, review date, policy | Calculate a completed summary |
| `Completed` | Finished summary | Retrieve the same completed result without recalculation |

`Ready` and `Completed` have internal constructors. `QuoteWorkflow.Calculate`
uses pattern matching over all three cases. An arbitrary raw array cannot be
passed to `Run` or used to construct `Ready` from the application.

```text
Pending -- Prepare / all rules pass --> Ready -- Calculate --> Completed
   |                                      |                       |
   +-- Calculate --> Failure              +-- pure calculation     +-- Calculate --> same Completed
   +-- Prepare / invalid input --> Failure
```

`Calculate(completed)` returns the **same instance**, without invoking the policy
again. Calculating the original immutable `Ready` again repeats a pure
calculation and produces equal values. A different policy/date needs a new
`Pending`; source-array edits cannot change an existing state's snapshot.
The core does not read files, console input, clocks, or random values.

## Automated tests and requirement mapping

The xUnit suite uses assertions, not console output, to compare actual and
expected results. `[Fact]` is one scenario; `[Theory]` produces a case for each
input. `dotnet test` returns a nonzero exit code on assertion failure.
Earlier `BookingTests.cs` and `TemporalCouplingTests.cs` are retained and expanded
by `ExceptionHandlingTests.cs`; cumulative coverage exceeds 10 tests.

**Latest run, 25 October 2026:** **73 passed, 0 failed, 0 skipped**, using SDK
**10.0.112**. Build: **0 warnings, 0 errors**. The suite includes 49 original
A5/A6 cases, 7 additional validation cases retained from GitHub, and 17 new
Assignment 7 tests. Clean-archive build, tests, and CLI checks are recorded in
`VERIFICATION.txt`.

| Requirement / test scenario | Implementation and automated evidence | Expected result |
|---|---|---|
| Task 1: protected Result cases | `Result<T>`; Result invariant tests | null success / blank error rejected; failed Value inaccessible |
| Complete successful chain | `BookingFiles.LoadQuotes`, `Input.Process`; success tests | 4 quotes; exact standard/group totals; A, B, F, C order |
| At least three domain failures | `ValidatedBookingBatch.Create`; failure tests | Passengers, departure, and duplicate Id each return a useful rule-specific error |
| Task 2: present/absent lookup | `Option<T>`, `BookingLookup.Find/Require`, `Input.FindBooking`; lookup tests | Existing booking is Some; missing is None, then a named Result failure |
| Legitimate primitive values | Option tests; `FalseIsValid`, required-field tests | Some(0), Some(false), enum zero and IsCancelled=false are not absence; missing fields fail |
| Map success | `Result.Map`, `Input.Process`, `QuoteWorkflow.Prepare`; composition tests | Callback receives successful value and produces the expected transformed value |
| Bind success | `Result.Bind`, parsing/validation/lookup chains; composition tests | Next operation receives the value and preserves its own Result |
| Failure propagation | Map/Bind and invalid-input tests | Exact error retained; supplied functions/pricing policy called 0 times |
| Task 4: missing file | `BookingFiles.Read`; temporary-path tests | Failure includes path and useful cause; state/file data remain unchanged |
| Task 4: malformed file | `Input.Parse`, file wrapper; controlled malformed-file tests | Invalid JSON is detected with file context; no partial result/update |
| Task 5: one final handler | `CommandLine.Run`, `Result.Match`; CLI tests | Success prints a result, failure prints an error without a total, correct exit codes |
| Safe A6 transitions | `QuoteState`, `QuoteWorkflow`; `TemporalCouplingTests` | Pending rejected, Ready calculates, repeated Completed keeps same reference and callback count |
| Immutability / state consistency | Factory and snapshot tests; file-boundary failure tests | Inputs, source file contents, and earlier successful results do not change |
| Preserved A5 pipeline | `BookingTests.cs` | Domain rules, filtering, rounding, deterministic sorting, both policies, and purity remain correct |

The 17 new test methods in `ExceptionHandlingTests.cs` cover:

| Test method | Expected assertion |
|---|---|
| `FileWorkflowComposesReadParseValidateCalculateAndProducesCorrectQuotes` | Controlled A/B file produces 49,000 + 264,600 = 313,600 USD, sorted A/B; file unchanged |
| `InvalidQuantityReportsTheRecordAndSkipsPricing` | Passenger-rule error names Record 1; pricing invoked 0 times |
| `DuplicateNormalizedIdReportsTheSecondRecordAndSkipsPricing` | Trimmed duplicate reports Record 2; pricing invoked 0 times |
| `WrongMissionDepartureReportsTheRuleAndSkipsPricing` | Schedule-rule error; pricing invoked 0 times |
| `UnpreparedWorkflowRejectsCalculationWithoutInvokingPolicy` | Pending fails; input unchanged; pricing invoked 0 times |
| `RealLookupHandlesPresentAndAbsentCasesAndRetainsMoonAndFalse` | Some retains Moon/false; unknown, null, and blank Ids produce None; Require converts absence to Failure |
| `PrimitiveSomeValuesRemainPresentForZeroFalseAndEmptyString` | 0, false, and empty string remain Some payloads |
| `MapPassesTheSuccessfulPayloadAndTransformsItsType` | Callback receives 4 and returns the expected string |
| `BindPassesThePayloadAndPreservesTheNextResult` | Callback receives 4; its success/failure Result is preserved |
| `FailurePropagatesUnchangedAndSkipsBothDependentCallbacks` | Same error; Map and Bind callback counts both 0 |
| `ResultRejectsContradictorySuccessAndUninformativeFailure` | null payload and blank error rejected; successful 0/false retained |
| `MatchCallsOnlyTheActiveCaseHandler` | Only the matching success/failure handler runs |
| `MissingFileReportsItsPathSkipsPolicyAndPreservesCompletedState` | Path in error, no created file, 0 pricing calls, previous completed result unchanged |
| `TruncatedSecondRecordReportsItsPathWithoutPricingOrPartialStateUpdate` | Invalid JSON with path, 0 pricing calls, original contents and previous completed result unchanged |
| `FileLookupFindsAnActualBookingAndTurnsAbsenceIntoFailure` | Existing A succeeds; unknown Id fails with useful context |
| `CommandLineFindReportsPresentAndMissingBookingsWithAppropriateExitCodes` | Found booking prints with exit 0; absence prints only an error with exit 1 |
| `CommandLineMalformedFileEmitsOnlyAnErrorAndNeverPartialQuotes` | Exit 1, empty successful output, error includes Invalid JSON and path |

File tests create controlled temporary directories/files and clean them up.
They do not depend on a data file that happens to exist on the developer's
computer. Project sample JSON is copied into the test output by the test project.
List the exact runnable test cases with:

```sh
dotnet test Astra.slnx --list-tests
```

## Team, contribution areas, and AI assistance

Group: **IT-2512**. Members: **Beknur Abdykhamit** and **Zhumazhan Miras**.
The submission owner is Zhumazhan Miras. Beknur's existing **Task 2 (Maybe/Option) of Assignment 7** implementation is
preserved, as confirmed by the submission owner.

| Member | Contribution area to review and defend for Assignment 7 | Tests and documentation to review |
|---|---|---|
| Beknur Abdykhamit | Existing Task 2: `Option<T>`, `BookingLookup.Find/Require`, `Input.FindBooking`, present/absent CLI lookup | Present/absent and primitive-value assertions; explanation of None versus valid false/zero and conversion to Result |
| Zhumazhan Miras | Remaining integration: protected Result handling, Map/Bind quote workflow, file/parse boundaries, CLI outcomes, preserved safe states | Domain failures, skipped-callback assertions, controlled file tests, state preservation, run instructions and submission verification |

The new integration, tests, and documentation were prepared with AI assistance.
The table identifies the implementation, tests, and documentation each member
reviews and defends; it does not claim independent authorship of the AI-assisted
additions. Each student must explain and modify their contribution during the
individual defense.

## Defense checklist

1. Run the normal batch, one of the three domain failures, the missing lookup,
   and a missing/malformed file. Identify the final Result case and exit code.
2. Trace `Read → Parse → Prepare → Calculate → Map(summary)` and the required
   lookup chain. Explain why Map returns a transformed value inside Result,
   while Bind accepts a function already returning Result.
3. Open the failure-propagation assertions. Show the exact error and a callback
   counter/flag proving that the dependent function was never invoked.
4. Match `Some`/`None` and explain why false/zero can be present values. Distinguish
   a missing passenger count from a present but invalid zero.
5. Identify `BookingFiles.Read` and `Input.Parse` as exception boundaries. Show
   immutable snapshots, the factory's local builder, and tests retaining an
   earlier completed summary/file content after failure.
6. Explain A6's three state cases and why repeating Completed does not run the
   pricing policy again. Run `demo` and the relevant tests.
7. Practice a small live change: add a regression test for a missing lookup Id,
   or modify a domain boundary and adjust the corresponding test. For example,
   lowering `MaximumPassengers` from 6 to 5 must accept 5 and reject 6; sample C
   and exact success totals would also need revision if the change is retained.
   Keep the submitted baseline at 6.

Кратко для защиты: **Map** меняет успешное значение, **Bind** запускает следующий
шаг, который тоже может вернуть ошибку. При Failure оба пропускают функцию.
**None** означает «запись не найдена»; **Some(false)** и **Some(0)** содержат
значение. Ошибки правил возвращает домен, исключения чтения и JSON обрабатываются
на границе приложения. Новое состояние принимается только после успеха.

## Submission

Archive: **`IT-2512_Zhumazhan_Miras_Assignment7.zip`**.
It includes all C# source and project files, `Astra.slnx`, `global.json`,
`Directory.Build.props`, tests, sample JSON, this README, and verification results.
It excludes generated `bin/` and `obj/`, IDE caches, `.git/`, credentials, and the
installed SDK. Extract it and run the build/run/test commands from its root.

Upload the **actual ZIP archive directly to the LMS/Moodle**. A GitHub/cloud
link or a document containing only a download link is not an accepted submission.
Each student uploads the project files and completes an individual defense.
The implementation is worth 8 points and the individual defense 7 points;
without an oral defense the score is 0 unless an alternative is officially approved.

Repository for continued work:
[mmionya/assignment-6-ifp](https://github.com/mmionya/assignment-6-ifp).
Keep the runnable project and tests for Assignment 8 and the final project.
