using Microsoft.EntityFrameworkCore;
using Sale.Application.Sales;
using Sale.Infrastructure.Persistence;
using Xunit;

namespace Sale.IntegrationTests;

[Trait("Category", "Database")]
public sealed class SaleManagementTests(SqlDatabaseFixture database)
    : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Another_tenants_sale_is_not_visible()
    {
        await using var db = database.CreateContext();
        var service = new SaleEventService(new SaleEventStore(db), TimeProvider.System);
        var result = await service.GetAsync("tenant-b", SqlDatabaseFixture.EventA,
            TestContext.Current.CancellationToken);

        Assert.Equal(SaleFailure.SaleNotFound, result.Failure);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Listing_is_scoped_to_the_requested_tenant()
    {
        await using var db = database.CreateContext();
        var service = new SaleEventService(new SaleEventStore(db), TimeProvider.System);
        var result = await service.ListAsync("tenant-a", 1, 1, TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal("tenant-a", Assert.Single(result.Value.Items).TenantId);
        Assert.Equal(SqlDatabaseFixture.EventA, result.Value.Items[0].Id);
    }

    [Fact]
    public async Task Creating_a_sale_persists_its_items_and_returns_the_same_data()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = database.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var service = new SaleEventService(new SaleEventStore(db), TimeProvider.System);
        var start = DateTimeOffset.UtcNow.AddMinutes(-1);
        var request = new CreateSaleEventRequest("New sale", start, start.AddHours(1),
            [new CreateSaleItemRequest("SHARED", 800m, 20)]);

        var created = await service.CreateAsync("tenant-a", request, cancellationToken);
        Assert.Null(created.Failure);
        Assert.NotNull(created.Value);
        var loaded = await service.GetAsync("tenant-a", created.Value.Id, cancellationToken);
        Assert.Null(loaded.Failure);
        Assert.NotNull(loaded.Value);
        Assert.Equal("Active", loaded.Value.Status);
        Assert.Equal("IRR", loaded.Value.CurrencyCode);
        var item = Assert.Single(loaded.Value.Items);
        Assert.Equal(20, item.RemainingQuantity);
        Assert.Equal(20, item.InitialQuantity);
        Assert.Equal(800m, item.DiscountedPrice);
    }

    [Fact]
    public async Task A_product_from_another_tenant_cannot_be_added_to_a_sale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = database.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var service = new SaleEventService(new SaleEventStore(db), TimeProvider.System);
        var start = DateTimeOffset.UtcNow;
        var request = new CreateSaleEventRequest("Invalid sale", start, start.AddHours(1),
            [new CreateSaleItemRequest("ONLY-B", 800m, 20)]);

        var result = await service.CreateAsync("tenant-a", request, cancellationToken);
        Assert.Equal(SaleFailure.Validation, result.Failure);
        Assert.NotNull(result.Errors);
        Assert.Contains("items[0].sku", result.Errors.Keys);
        Assert.Equal(1, await db.SaleEvents.CountAsync(sale => sale.TenantId == "tenant-a", cancellationToken));
    }
}
