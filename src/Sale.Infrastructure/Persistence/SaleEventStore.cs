using Microsoft.EntityFrameworkCore;
using Sale.Application.Sales;
using Sale.Domain.Entities;

namespace Sale.Infrastructure.Persistence;

public sealed class SaleEventStore(SaleDbContext db) : ISaleEventStore
{
    public Task<Tenant?> FindTenantAsync(string tenantId, CancellationToken cancellationToken) =>
        db.Tenants.AsNoTracking().SingleOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);

    public async Task<IReadOnlyList<Product>> FindProductsAsync(
        string tenantId, IReadOnlyList<string> skus, CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking()
            .Where(product => product.TenantId == tenantId && skus.Contains(product.Sku))
            .ToListAsync(cancellationToken);

    public async Task AddAsync(
        SaleEvent sale, IReadOnlyList<SaleEventItem> items, CancellationToken cancellationToken)
    {
        if (items.Any(item => item.TenantId != sale.TenantId || item.SaleEventId != sale.Id))
            throw new InvalidOperationException("Sale items must belong to the same tenant and sale.");

        db.SaleEvents.Add(sale);
        db.SaleEventItems.AddRange(items);
        // EF commits the event and all of its items in one SaveChanges transaction.
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SaleEventData?> FindAsync(
        string tenantId, Guid saleEventId, CancellationToken cancellationToken)
    {
        var sale = await db.SaleEvents.AsNoTracking().SingleOrDefaultAsync(
            sale => sale.TenantId == tenantId && sale.Id == saleEventId, cancellationToken);
        if (sale is null)
            return null;

        var items = await db.SaleEventItems.AsNoTracking().Where(
            item => item.TenantId == tenantId && item.SaleEventId == saleEventId)
            .ToListAsync(cancellationToken);
        return new(sale, items);
    }

    public async Task<SalePageData> ListAsync(
        string tenantId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.SaleEvents.AsNoTracking().Where(sale => sale.TenantId == tenantId);
        var total = await query.CountAsync(cancellationToken);
        var sales = await query.OrderBy(sale => sale.StartsAt).ThenBy(sale => sale.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        if (sales.Count == 0)
            return new([], total);

        var ids = sales.Select(sale => sale.Id).ToArray();
        var items = await db.SaleEventItems.AsNoTracking()
            .Where(item => item.TenantId == tenantId && ids.Contains(item.SaleEventId))
            .ToListAsync(cancellationToken);
        var bySale = items.ToLookup(item => item.SaleEventId);
        return new(sales.Select(sale => new SaleEventData(sale, bySale[sale.Id].ToArray())).ToArray(), total);
    }
}
