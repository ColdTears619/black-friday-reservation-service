using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sale.Infrastructure.Persistence;
using Xunit;

namespace Sale.IntegrationTests;

[Trait("Category", "Database")]
public sealed class SeedTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Repeating_seed_preserves_stock_and_sale_times()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = database.CreateContext();
        var initializer = new DatabaseInitializer(db, new ConfigurationBuilder().Build(),
            TimeProvider.System, NullLogger<DatabaseInitializer>.Instance);
        var reference = new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
        var activeId = Guid.Parse("00000001-0000-0000-0000-000000000001");

        await initializer.SeedAsync(reference, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [SaleEventItems] SET [RemainingQuantity] = 1
            WHERE [TenantId] = N'tenant-electronics'
                AND [SaleEventId] = {activeId} AND [Sku] = N'TV-65-4K';
            """, cancellationToken);
        db.ChangeTracker.Clear();

        await initializer.SeedAsync(reference.AddDays(7), cancellationToken);
        var tenants = new[] { "tenant-electronics", "tenant-fashion" };
        Assert.Equal(2, await db.Tenants.CountAsync(tenant => tenants.Contains(tenant.Id), cancellationToken));
        Assert.Equal(10, await db.Products.CountAsync(product => tenants.Contains(product.TenantId), cancellationToken));
        Assert.Equal(6, await db.SaleEvents.CountAsync(sale => tenants.Contains(sale.TenantId), cancellationToken));
        Assert.Equal(30, await db.SaleEventItems.CountAsync(item => tenants.Contains(item.TenantId), cancellationToken));

        var activeSale = await db.SaleEvents.AsNoTracking().SingleAsync(
            sale => sale.TenantId == "tenant-electronics" && sale.Id == activeId, cancellationToken);
        var item = await db.SaleEventItems.AsNoTracking().SingleAsync(
            item => item.TenantId == "tenant-electronics" && item.SaleEventId == activeId && item.Sku == "TV-65-4K",
            cancellationToken);
        Assert.Equal(reference.AddHours(-1), activeSale.StartsAt);
        Assert.Equal(reference.AddDays(1), activeSale.EndsAt);
        Assert.Equal(1, item.RemainingQuantity);

        var sales = await db.SaleEvents.AsNoTracking().Where(sale => tenants.Contains(sale.TenantId))
            .ToListAsync(cancellationToken);
        Assert.Equal(2, sales.Count(sale => sale.StartsAt <= reference && reference < sale.EndsAt));
        Assert.Equal(2, sales.Count(sale => sale.StartsAt > reference));
        Assert.Equal(2, sales.Count(sale => sale.EndsAt <= reference));
    }
}
