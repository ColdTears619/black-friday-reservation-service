# 3. Project structure

```text
BlackFridaySale.sln
Directory.Build.props
.config/dotnet-tools.json
src/
  Sale.Domain/
    Entities/
  Sale.Application/
    Sales/
    Reservations/
  Sale.Infrastructure/
    Persistence/
      Configurations/
      Migrations/
      DatabaseInitializer.cs
      SaleDbContext.cs
      SaleEventStore.cs
    Reservations/
      ReservationStore.cs
      ReservationOptions.cs
      ReservationExpirationWorker.cs
    DependencyInjection.cs
  Sale.Api/
    Endpoints/
    Logging/
    Properties/launchSettings.json
    appsettings.Development.json
    Program.cs
  Sale.Worker/
    Properties/launchSettings.json
    appsettings.Development.json
    Program.cs
tests/
  Sale.IntegrationTests/
scripts/
  check_reservation_capacity.py
doc/
README.md
```

## Where to make or review a change

| Area | Main location |
| --- | --- |
| Entity fields and states | [Domain entities](../src/Sale.Domain/Entities) |
| Sale validation and service flow | [SaleEventService.cs](../src/Sale.Application/Sales/SaleEventService.cs) |
| Reservation validation | [ReservationService.cs](../src/Sale.Application/Reservations/ReservationService.cs) |
| Request, response, and storage contracts | [ReservationContracts.cs](../src/Sale.Application/Reservations/ReservationContracts.cs), [SaleContracts.cs](../src/Sale.Application/Sales/SaleContracts.cs) |
| Sale reads and writes | [SaleEventStore.cs](../src/Sale.Infrastructure/Persistence/SaleEventStore.cs) |
| Atomic hold, confirmation, and release | [ReservationStore.cs](../src/Sale.Infrastructure/Reservations/ReservationStore.cs) |
| Database keys and constraints | [Configurations](../src/Sale.Infrastructure/Persistence/Configurations) |
| Database schema history | [Migrations](../src/Sale.Infrastructure/Persistence/Migrations) |
| Demo seed records | [DatabaseInitializer.cs](../src/Sale.Infrastructure/Persistence/DatabaseInitializer.cs) |
| Expiry loop and failure logging | [ReservationExpirationWorker.cs](../src/Sale.Infrastructure/Reservations/ReservationExpirationWorker.cs) |
| HTTP routes and status mapping | [API endpoints](../src/Sale.Api/Endpoints) |
| Dependency registration and option defaults | [DependencyInjection.cs](../src/Sale.Infrastructure/DependencyInjection.cs) |
| API port, development logs, and hold duration | [API development settings](../src/Sale.Api/appsettings.Development.json) |
| Worker interval and batch size | [Worker development settings](../src/Sale.Worker/appsettings.Development.json) |
| Repeatable concurrency demo | [Capacity script](../scripts/check_reservation_capacity.py) |

The test project contains both unit tests and SQL integration tests. `SqlDatabaseFixture` creates temporary test databases. Tests use category filters so unit tests can run without SQL. See the [testing guide](testing.md).

Connection credentials belong in .NET User Secrets or environment variables, not these source files. See [setup and seed data](setup-and-seed.md).

Next: [Trade-offs](04-trade-offs.md).
