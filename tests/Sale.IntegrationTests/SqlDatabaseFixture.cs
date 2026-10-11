using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Sale.Infrastructure.Persistence;
using Xunit;

namespace Sale.IntegrationTests;

public sealed class SqlDatabaseFixture : IAsyncLifetime
{
    public static readonly Guid EventA =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid EventB =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private DbContextOptions<SaleDbContext>? _options;

    public SaleDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var options = _options ?? throw new InvalidOperationException("The test database has not been initialized.");
        return new SaleDbContext(interceptors.Length == 0 ? options
            : new DbContextOptionsBuilder<SaleDbContext>(options).AddInterceptors(interceptors).Options);
    }

    public async ValueTask InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<SqlDatabaseFixture>()
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Configure ConnectionStrings:SqlServer before running tests.");
        }

        var connection = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = $"BlackFridaySale_Tests_{Guid.NewGuid():N}"
        };

        _options = new DbContextOptionsBuilder<SaleDbContext>()
            .UseSqlServer(connection.ConnectionString)
            .Options;

        await using var db = CreateContext();

        if (!db.Database.GetMigrations().Any())
        {
            throw new InvalidOperationException(
                "Generate the initial migration before running tests.");
        }

        await db.Database.MigrateAsync(CancellationToken.None);

        var startsAt = new DateTimeOffset(
            2026, 11, 27, 9, 0, 0, TimeSpan.Zero);

        var endsAt = startsAt.AddMinutes(30);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Tenants] ([Id], [Name], [CurrencyCode])
            VALUES
                (N'tenant-a', N'Store A', N'IRR'),
                (N'tenant-b', N'Store B', N'IRR');

            INSERT INTO [Products]
                ([TenantId], [Sku], [Name], [RegularPrice])
            VALUES
                (N'tenant-a', N'SHARED', N'Product A', 1000),
                (N'tenant-b', N'SHARED', N'Product B', 1000),
                (N'tenant-b', N'ONLY-B', N'Exclusive B', 1000);

            INSERT INTO [SaleEvents]
                ([TenantId], [Id], [Name], [StartsAt], [EndsAt])
            VALUES
                (N'tenant-a', {EventA}, N'Sale A', {startsAt}, {endsAt}),
                (N'tenant-b', {EventB}, N'Sale B', {startsAt}, {endsAt});

            INSERT INTO [SaleEventItems]
                ([TenantId], [SaleEventId], [Sku],
                 [DiscountedPrice], [InitialQuantity], [RemainingQuantity])
            VALUES
                (N'tenant-a', {EventA}, N'SHARED', 900, 10, 5);
            """, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_options is null)
        {
            return;
        }

        await using var db = CreateContext();

        await db.Database.EnsureDeletedAsync(CancellationToken.None);
    }
}
