using Sale.Application.Sales;
using Sale.Domain.Entities;
using Xunit;

namespace Sale.IntegrationTests;

[Trait("Category", "Unit")]
public sealed class SaleEventServiceTests
{
    private static readonly DateTimeOffset StartsAt =
        new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, "Upcoming")]
    [InlineData(0, "Active")]
    [InlineData(59, "Active")]
    [InlineData(60, "Expired")]
    public async Task Sale_status_obeys_inclusive_start_and_exclusive_end(int seconds, string expected)
    {
        var service = new SaleEventService(new MemoryStore(), new FixedClock(StartsAt.AddSeconds(seconds)));
        var result = await service.CreateAsync("tenant-a", Request(), TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.NotNull(result.Value);
        Assert.Equal(expected, result.Value.Status);
    }

    [Fact]
    public async Task New_sale_normalizes_time_and_allocates_the_requested_stock()
    {
        var store = new MemoryStore();
        var service = new SaleEventService(store, new FixedClock(StartsAt));
        var request = Request() with
        {
            StartsAt = StartsAt.ToOffset(TimeSpan.FromMinutes(210)),
            EndsAt = StartsAt.AddMinutes(1).ToOffset(TimeSpan.FromMinutes(210))
        };
        var result = await service.CreateAsync("tenant-a", request, TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.NotNull(result.Value);
        Assert.Equal(TimeSpan.Zero, result.Value.StartsAt.Offset);
        Assert.Equal(StartsAt, result.Value.StartsAt);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(10, item.InitialQuantity);
        Assert.Equal(10, item.RemainingQuantity);
        Assert.Single(store.Sales);
    }

    [Fact]
    public async Task Duplicate_skus_are_rejected_before_a_sale_is_saved()
    {
        var store = new MemoryStore();
        var service = new SaleEventService(store, new FixedClock(StartsAt));
        var request = Request() with
        {
            Items = [new("SKU-1", 900m, 10), new("sku-1", 800m, 20)]
        };
        var result = await service.CreateAsync("tenant-a", request, TestContext.Current.CancellationToken);

        Assert.Equal(SaleFailure.Validation, result.Failure);
        Assert.Empty(store.Sales);
    }

    [Fact]
    public async Task Price_is_not_silently_rounded_before_persistence()
    {
        var store = new MemoryStore();
        var service = new SaleEventService(store, new FixedClock(StartsAt));
        var request = Request() with { Items = [new("SKU-1", 900.001m, 10)] };
        var result = await service.CreateAsync("tenant-a", request, TestContext.Current.CancellationToken);

        Assert.Equal(SaleFailure.Validation, result.Failure);
        Assert.Empty(store.Sales);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_stock_is_rejected(int quantity)
    {
        var service = new SaleEventService(new MemoryStore(), new FixedClock(StartsAt));
        var request = Request() with { Items = [new("SKU-1", 900m, quantity)] };
        var result = await service.CreateAsync("tenant-a", request, TestContext.Current.CancellationToken);
        Assert.Equal(SaleFailure.Validation, result.Failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_time_range_is_rejected(int seconds)
    {
        var service = new SaleEventService(new MemoryStore(), new FixedClock(StartsAt));
        var request = Request() with { EndsAt = StartsAt.AddSeconds(seconds) };
        var result = await service.CreateAsync("tenant-a", request, TestContext.Current.CancellationToken);
        Assert.Equal(SaleFailure.Validation, result.Failure);
    }

    [Fact]
    public async Task Missing_tenant_cannot_create_a_sale()
    {
        var store = new MemoryStore();
        var service = new SaleEventService(store, new FixedClock(StartsAt));
        var result = await service.CreateAsync("unknown", Request(), TestContext.Current.CancellationToken);
        Assert.Equal(SaleFailure.TenantNotFound, result.Failure);
        Assert.Empty(store.Sales);
    }

    private static CreateSaleEventRequest Request() =>
        new("Sale", StartsAt, StartsAt.AddMinutes(1), [new("SKU-1", 900m, 10)]);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MemoryStore : ISaleEventStore
    {
        public List<SaleEventData> Sales { get; } = [];

        public Task<Tenant?> FindTenantAsync(string tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<Tenant?>(tenantId == "tenant-a"
                ? new Tenant { Id = tenantId, Name = "Store A", CurrencyCode = "IRR" }
                : null);

        public Task<IReadOnlyList<Product>> FindProductsAsync(
            string tenantId, IReadOnlyList<string> skus, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Product>>(
                tenantId == "tenant-a" && skus.Contains("SKU-1", StringComparer.OrdinalIgnoreCase)
                    ? [new Product { TenantId = tenantId, Sku = "SKU-1", Name = "Product", RegularPrice = 1000m }]
                    : []);

        public Task AddAsync(SaleEvent sale, IReadOnlyList<SaleEventItem> items, CancellationToken cancellationToken)
        {
            Sales.Add(new(sale, items));
            return Task.CompletedTask;
        }

        public Task<SaleEventData?> FindAsync(string tenantId, Guid saleEventId, CancellationToken cancellationToken) =>
            Task.FromResult(Sales.SingleOrDefault(sale => sale.Event.TenantId == tenantId && sale.Event.Id == saleEventId));

        public Task<SalePageData> ListAsync(string tenantId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var matches = Sales.Where(sale => sale.Event.TenantId == tenantId).ToArray();
            return Task.FromResult(new SalePageData(matches.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), matches.Length));
        }
    }
}
