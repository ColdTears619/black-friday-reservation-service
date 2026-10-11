using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sale.Domain.Entities;

namespace Sale.Infrastructure.Persistence;

public sealed class DatabaseInitializer(
    SaleDbContext db,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(new EventId(2000, "DatabaseInitializationStarted"),
            "Applying database migrations and initializing seed data");

        await db.Database.MigrateAsync(cancellationToken);

        var configuredTime = configuration["Seed:ReferenceTimeUtc"];
        var referenceTime = string.IsNullOrWhiteSpace(configuredTime)
            ? timeProvider.GetUtcNow()
            : DateTimeOffset.Parse(configuredTime, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        await SeedAsync(referenceTime.ToUniversalTime(), cancellationToken);
    }

    public async Task SeedAsync(DateTimeOffset referenceTime, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'BlackFridaySale:Seed',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;
            IF @result < 0 THROW 50001, 'Could not acquire the seed lock.', 1;
            """, cancellationToken);

        var definitions = GetDefinitions();
        for (var tenantIndex = 0; tenantIndex < definitions.Length; tenantIndex++)
        {
            var definition = definitions[tenantIndex];
            if (!await db.Tenants.AnyAsync(tenant => tenant.Id == definition.Id, cancellationToken))
                db.Tenants.Add(new Tenant { Id = definition.Id, Name = definition.Name, CurrencyCode = "IRR" });

            var existingSkus = await db.Products.Where(product => product.TenantId == definition.Id)
                .Select(product => product.Sku).ToListAsync(cancellationToken);
            var skuSet = existingSkus.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var product in definition.Products)
                if (!skuSet.Contains(product.Sku))
                    db.Products.Add(new Product
                    {
                        TenantId = definition.Id,
                        Sku = product.Sku,
                        Name = product.Name,
                        RegularPrice = product.Price
                    });

            var existingSales = await db.SaleEvents.Where(sale => sale.TenantId == definition.Id)
                .ToDictionaryAsync(sale => sale.Id, cancellationToken);
            var existingItems = await db.SaleEventItems.Where(item => item.TenantId == definition.Id)
                .Select(item => new { item.SaleEventId, item.Sku }).ToListAsync(cancellationToken);
            var itemKeys = existingItems.Select(item => (item.SaleEventId, item.Sku)).ToHashSet();

            // Preserve the original anchor on repeat runs instead of extending a running sale.
            var activeId = EventIdFor(tenantIndex, 0);
            var anchor = existingSales.TryGetValue(activeId, out var activeSale)
                ? activeSale.StartsAt.AddHours(1)
                : referenceTime;

            for (var eventIndex = 0; eventIndex < 3; eventIndex++)
            {
                var id = EventIdFor(tenantIndex, eventIndex);
                var (label, start, end, quantity) = eventIndex switch
                {
                    0 => ("Active", anchor.AddHours(-1), anchor.AddDays(1), 100),
                    1 => ("Upcoming", anchor.AddDays(2), anchor.AddDays(3), 150),
                    _ => ("Expired", anchor.AddDays(-2), anchor.AddDays(-1), 50)
                };

                if (!existingSales.ContainsKey(id))
                    db.SaleEvents.Add(new SaleEvent
                    {
                        TenantId = definition.Id,
                        Id = id,
                        Name = $"{definition.Name} - {label} sale",
                        StartsAt = start,
                        EndsAt = end
                    });

                foreach (var product in definition.Products)
                    if (!itemKeys.Contains((id, product.Sku)))
                        db.SaleEventItems.Add(new SaleEventItem
                        {
                            TenantId = definition.Id,
                            SaleEventId = id,
                            Sku = product.Sku,
                            DiscountedPrice = decimal.Round(product.Price * 0.8m, 2),
                            InitialQuantity = quantity,
                            RemainingQuantity = quantity
                        });
            }
        }

        var inserted = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(new EventId(2001, "SeedCompleted"),
            "Seed completed: {InsertedRecords} records inserted; reference time {ReferenceTimeUtc}",
            inserted, referenceTime);
    }

    private static Guid EventIdFor(int tenantIndex, int eventIndex) =>
        Guid.Parse($"{tenantIndex + 1:D8}-0000-0000-0000-{eventIndex + 1:D12}");

    private static TenantSeed[] GetDefinitions() =>
    [
        new("tenant-electronics", "Electronics Store",
        [
            new("TV-65-4K", "65-inch 4K Television", 350_000_000m),
            new("LAPTOP-15", "15-inch Laptop", 600_000_000m),
            new("PHONE-256", "256GB Smartphone", 420_000_000m),
            new("HEADPHONE-NC", "Noise-cancelling Headphones", 45_000_000m),
            new("CONSOLE-1TB", "1TB Game Console", 380_000_000m)
        ]),
        new("tenant-fashion", "Fashion Store",
        [
            new("TSHIRT-BLK-M", "Black T-shirt - Medium", 6_000_000m),
            new("JEANS-BLUE-32", "Blue Jeans - Size 32", 18_000_000m),
            new("SNEAKER-WHT-42", "White Sneakers - Size 42", 28_000_000m),
            new("JACKET-WINTER-L", "Winter Jacket - Large", 45_000_000m),
            new("HOODIE-GRAY-M", "Gray Hoodie - Medium", 15_000_000m)
        ])
    ];

    private sealed record TenantSeed(string Id, string Name, ProductSeed[] Products);
    private sealed record ProductSeed(string Sku, string Name, decimal Price);
}
