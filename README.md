# ASTRA

A C# console application for a space travel agency. It calculates prices,
creates reservations, transfers them between flights, and cancels them with refunds.
Flights and reservations are stored in JSON.

Team: Zhumazhan Miras and Abdykhamit Beknur, IT-2512.

Repository: https://github.com/mmionya/assignment-6-ifp

Branch: `main`. The submission archive's top-level README records the exact
source commit shared by both students and both reports.

## Run

Requires the .NET 10 SDK and an x64 runtime. Windows x64 is verified.
NuGet access is needed to restore test dependencies.

```powershell
dotnet build Astra.slnx
dotnet test Astra.slnx
Copy-Item samples/agency.json demo-state.json -Force
dotnet run --project Astra.App -- agency help
```

## Example

Run these commands in order after copying the sample:

```powershell
dotnet run --project Astra.App -- agency flights demo-state.json 2030-06-01
dotnet run --project Astra.App -- agency quote demo-state.json MOON-1 4 premium group10 2030-06-01
dotnet run --project Astra.App -- agency reserve demo-state.json NEW MOON-1 4 premium group10 2030-06-01
dotnet run --project Astra.App -- agency find demo-state.json NEW
dotnet run --project Astra.App -- agency transfer demo-state.json NEW MOON-2 premium group10 2030-06-02
dotnet run --project Astra.App -- agency cancel demo-state.json NEW 2030-06-08
dotnet run --project Astra.App -- agency reports demo-state.json 2030-06-08 demo-report.csv
```

The quote is 238140.00 USD. Transfer changes the price to 252720.00 USD,
with 14580.00 USD due. Cancellation in this example refunds 252720.00 USD.
Amounts are calculations only; the application does not process payments.

## Rules

- Each reservation has 1-6 passengers and a unique ID.
- Classes: Explorer, Premium (x1.35), First (x1.8).
- Policies: Standard, Group10 (10% off for 4+ passengers), Family5 (5% off for 2+).
- Booking and changes must happen before departure.
- Cancellation refunds 100% at least 7 days before departure, otherwise 50%.
- Transfer requires another available flight to the same destination.
- Failed operations preserve the previous state. Cancelled IDs cannot be reused.
- Reports show flight availability and contract amounts by destination.

## Projects

- `Astra.Core`: immutable models, Result/Option, pricing, booking, changes and LINQ reports.
- `Astra.App`: commands, JSON storage, file handling, CSV output and native C integration.
- `Astra.Tests`: business rules, commands, persistence, export and native-call checks.
- `samples/agency.json`: sample flights and reservations.

Pricing functions are passed as delegates. `Map` and `Bind` compose operations,
factories validate objects, and successful changes return new state snapshots.
`ReservationExtensions` provides flight filtering and error context.

## Native library

The included C library checks whether a reservation meets the group-size threshold.
The build copies the Windows DLL or Linux shared library beside the application.
To rebuild it with an x64 GCC toolchain, run the command for your platform:

```powershell
gcc -std=c11 -O2 -Wall -Wextra -Werror -shared Astra.App/native/astra_native.c -o Astra.App/native/runtimes/win-x64/native/astra_native.dll
```

```sh
gcc -std=c11 -O2 -Wall -Wextra -Werror -fPIC -shared Astra.App/native/astra_native.c -o Astra.App/native/runtimes/linux-x64/native/libastra_native.so
```

Linux x64 binaries are included but have not been verified in this environment.
