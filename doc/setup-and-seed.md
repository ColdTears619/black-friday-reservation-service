# Setup and seed data

Run the commands below from the repository root. They use a Linux/macOS-style shell. You need the .NET 8 SDK and a reachable SQL Server instance. Python 3 is needed only for the capacity demo.

There are no Dockerfiles or Docker Compose files yet. The current development setup uses an existing SQL Server container exposed at `localhost,1433`. API and worker run as separate .NET processes.

## 1. Restore dependencies

```sh
dotnet restore BlackFridaySale.sln
dotnet tool restore
```

## 2. Configure the SQL connection

Replace the user and password placeholders before running this command:

```sh
dotnet user-secrets set 'ConnectionStrings:SqlServer' 'Server=localhost,1433;Database=BlackFridaySale;User Id=<YOUR_SQL_USER>;Password=<YOUR_SQL_PASSWORD>;Encrypt=True;TrustServerCertificate=True' --project src/Sale.Api/Sale.Api.csproj
```

The API, worker, and test project share the same User Secrets configuration. Set the value once for local development. Their development launch profiles load the secrets. Tests also load them explicitly.

An environment variable named `ConnectionStrings__SqlServer` is another configuration option. Use a valid server certificate for deployed environments; `TrustServerCertificate=True` is for this local setup.

## 3. Create the schema and add seed data

This command **writes to the configured database**: it applies migrations, then inserts missing seed records. The SQL account needs permission to create the database if it does not exist and apply schema changes.

```sh
dotnet run --project src/Sale.Api/Sale.Api.csproj -- --Database:InitializeOnStartup=true
```

The API keeps running after initialization. Open `http://localhost:5080/swagger`. Stop another API instance first if it already uses port 5080.

For later runs, initialization is disabled by default:

```sh
dotnet run --project src/Sale.Api/Sale.Api.csproj
```

## 4. Start the expiry worker

In a second terminal:

```sh
dotnet run --project src/Sale.Worker/Sale.Worker.csproj
```

The worker expects the database schema to exist. It does not migrate or seed the database at startup.

## Settings

| Setting | Current development value | Meaning |
| --- | --- | --- |
| API `Urls` | `http://localhost:5080` | API address |
| `Reservations:HoldSeconds` | `60` in API settings | Lifetime of a new hold |
| `Reservations:ExpirationPollSeconds` | `5` in worker settings | Interval between expiry checks |
| `Reservations:ExpirationBatchSize` | `100` in worker settings | Maximum candidates per sweep |
| `Database:InitializeOnStartup` | Unset / false | Apply migrations and seed on API startup |

The fallback hold duration is 600 seconds if no setting is supplied. For a longer manual test, restart the API with this override:

```sh
dotnet run --project src/Sale.Api/Sale.Api.csproj -- --Reservations:HoldSeconds=600
```

This affects new holds only. The worker uses the stored `ExpiresAt`; it does not recalculate old deadlines from its own configuration. Sale and reservation times are UTC. Reservation checks use SQL Server time; sale status display uses the application clock.

## Where are the seeds?

Seed records are defined in [DatabaseInitializer.cs](../src/Sale.Infrastructure/Persistence/DatabaseInitializer.cs), mainly in `GetDefinitions()` and `SeedAsync()`.

They are **not inside the migration files**. [Migrations](../src/Sale.Infrastructure/Persistence/Migrations) create and update the schema. `InitializeAsync()` first runs migrations and then calls the seed logic.

On an empty database, the seed creates:

- Two tenants: `tenant-electronics` and `tenant-fashion`, both using `IRR`.
- Five products per tenant: ten SKUs in total.
- Three sales per tenant: active, upcoming, and expired relative to the initial seed reference time.
- Five sale items in each sale, each with a 20% discount.

| Sale type | Start relative to reference time | End relative to reference time | Quantity per SKU |
| --- | --- | --- | --- |
| Active | 1 hour before | 1 day after | 100 |
| Upcoming | 2 days after | 3 days after | 150 |
| Expired | 2 days before | 1 day before | 50 |

The electronics SKUs are `TV-65-4K`, `LAPTOP-15`, `PHONE-256`, `HEADPHONE-NC`, and `CONSOLE-1TB`. The fashion SKUs are `TSHIRT-BLK-M`, `JEANS-BLUE-32`, `SNEAKER-WHT-42`, `JACKET-WINTER-L`, and `HOODIE-GRAY-M`.

Tenant and sale IDs are stable. For example, the initially active electronics sale is `00000001-0000-0000-0000-000000000001`. Its name does not mean it will remain active forever.

The initializer adds missing data inside a transaction with a seed lock. It does not reset existing quantities or dates. Running it again will not refill used sales or restart expired sales.

By default, the initial reference time is the current UTC time. Supply `Seed:ReferenceTimeUtc` as an ISO 8601 UTC timestamp when you need repeatable initial dates. It does not rewrite existing sales. For a fresh capacity demo at a later date, use the [test script](testing.md#capacity-demo).

## Apply migrations without seed data

If you only want schema changes, use the local EF tool:

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project src/Sale.Infrastructure/Sale.Infrastructure.csproj --startup-project src/Sale.Api/Sale.Api.csproj -- --Database:InitializeOnStartup=false
```

This writes schema changes but does **not** run `SeedAsync()`. Use the initialization command above when seed data is also needed.

Next: [Testing](testing.md).
