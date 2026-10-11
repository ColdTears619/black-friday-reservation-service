using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sale.Infrastructure.Persistence;
using Xunit;

namespace Sale.IntegrationTests;

[Trait("Category", "Database")]
public sealed class SchemaConstraintTests(
    SqlDatabaseFixture database)
    : IClassFixture<SqlDatabaseFixture>
{
    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public Task Invalid_remaining_quantity_is_rejected(int quantity)
    {
        return RejectAsync(
            $"""
            UPDATE [SaleEventItems]
            SET [RemainingQuantity] = {quantity}
            WHERE [TenantId] = N'tenant-a';
            """,
            "CK_SaleEventItems_RemainingQuantity");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public Task Remaining_quantity_boundaries_are_allowed(int quantity)
    {
        return WithinTransactionAsync(async (db, cancellationToken) =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE [SaleEventItems]
                SET [RemainingQuantity] = {quantity}
                WHERE [TenantId] = N'tenant-a';
                """,
                cancellationToken);

            var item = await db.SaleEventItems
                .AsNoTracking()
                .SingleAsync(cancellationToken);

            Assert.Equal(quantity, item.RemainingQuantity);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public Task Invalid_sale_time_range_is_rejected(int minutes)
    {
        return RejectAsync(
            $"""
            UPDATE [SaleEvents]
            SET [EndsAt] = DATEADD(minute, {minutes}, [StartsAt])
            WHERE [TenantId] = N'tenant-a';
            """,
            "CK_SaleEvents_TimeRange");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public Task Non_positive_regular_price_is_rejected(int price)
    {
        return RejectAsync(
            $"""
            UPDATE [Products]
            SET [RegularPrice] = {price}
            WHERE [TenantId] = N'tenant-a';
            """,
            "CK_Products_RegularPrice");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public Task Non_positive_sale_price_is_rejected(int price)
    {
        return RejectAsync(
            $"""
            UPDATE [SaleEventItems]
            SET [DiscountedPrice] = {price}
            WHERE [TenantId] = N'tenant-a';
            """,
            "CK_SaleEventItems_DiscountedPrice");
    }

    [Fact]
    public Task Zero_initial_quantity_is_rejected()
    {
        return RejectAsync(
            $"""
            UPDATE [SaleEventItems]
            SET [InitialQuantity] = 0, [RemainingQuantity] = 0
            WHERE [TenantId] = N'tenant-a';
            """,
            "CK_SaleEventItems_InitialQuantity");
    }

    [Fact]
    public Task Duplicate_sku_in_the_same_tenant_is_rejected()
    {
        return RejectAsync(
            $"""
            INSERT INTO [Products]
                ([TenantId], [Sku], [Name], [RegularPrice])
            VALUES
                (N'tenant-a', N'SHARED', N'Duplicate', 1000);
            """,
            "PK_Products",
            expectedErrorNumber: 2627);
    }

    [Fact]
    public async Task Same_sku_in_different_tenants_is_allowed()
    {
        await using var db = database.CreateContext();

        var count = await db.Products.CountAsync(
            product => product.Sku == "SHARED",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, count);
    }

    [Fact]
    public Task Product_from_another_tenant_is_rejected()
    {
        return RejectAsync(
            $"""
            UPDATE [SaleEventItems]
            SET [Sku] = N'ONLY-B'
            WHERE [TenantId] = N'tenant-a';
            """,
            "FK_SaleEventItems_Products_TenantId_Sku");
    }

    [Fact]
    public Task Sale_event_from_another_tenant_is_rejected()
    {
        return RejectAsync(
            $"""
            UPDATE [SaleEventItems]
            SET [SaleEventId] = {SqlDatabaseFixture.EventB}
            WHERE [TenantId] = N'tenant-a';
            """,
            "FK_SaleEventItems_SaleEvents_TenantId_SaleEventId");
    }

    [Fact]
    public Task Duplicate_item_in_the_same_sale_is_rejected()
    {
        return RejectAsync(
            $"""
            INSERT INTO [SaleEventItems]
                ([TenantId], [SaleEventId], [Sku],
                 [DiscountedPrice], [InitialQuantity], [RemainingQuantity])
            SELECT
                [TenantId], [SaleEventId], [Sku],
                [DiscountedPrice], [InitialQuantity], [RemainingQuantity]
            FROM [SaleEventItems]
            WHERE [TenantId] = N'tenant-a';
            """,
            "PK_SaleEventItems",
            expectedErrorNumber: 2627);
    }

    private Task RejectAsync(
        FormattableString command,
        string constraintName,
        int expectedErrorNumber = 547)
    {
        return WithinTransactionAsync(async (db, cancellationToken) =>
        {
            var exception = await Assert.ThrowsAsync<SqlException>(
                () => db.Database.ExecuteSqlInterpolatedAsync(
                    command,
                    cancellationToken));

            Assert.Equal(expectedErrorNumber, exception.Number);
            Assert.Contains(constraintName, exception.Message);
        });
    }

    private async Task WithinTransactionAsync(
        Func<SaleDbContext, CancellationToken, Task> action)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var db = database.CreateContext();

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        await action(db, cancellationToken);

        // Disposing the uncommitted transaction rolls back test changes.
    }
}
