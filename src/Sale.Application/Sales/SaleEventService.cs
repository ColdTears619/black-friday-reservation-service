using Sale.Domain.Entities;

namespace Sale.Application.Sales;

public sealed class SaleEventService(ISaleEventStore store, TimeProvider timeProvider)
{
    private const decimal MaximumPrice = 9999999999999999.99m;

    public async Task<SaleResult<SaleEventResponse>> CreateAsync(
        string tenantId, CreateSaleEventRequest request, CancellationToken cancellationToken)
    {
        var errors = Validate(tenantId, request);
        if (errors.Count > 0)
            return new(null, SaleFailure.Validation, errors);

        var tenant = await store.FindTenantAsync(tenantId, cancellationToken);
        if (tenant is null)
            return new(null, SaleFailure.TenantNotFound);

        var requestedItems = request.Items!;
        var skus = requestedItems.Select(item => item!.Sku!.Trim()).ToArray();
        var products = await store.FindProductsAsync(tenant.Id, skus, cancellationToken);
        var bySku = products.ToDictionary(product => product.Sku, StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < requestedItems.Count; index++)
        {
            var item = requestedItems[index]!;
            if (!bySku.TryGetValue(item.Sku!.Trim(), out var product))
                errors[$"items[{index}].sku"] = ["The product does not exist in this tenant."];
            else if (item.DiscountedPrice >= product.RegularPrice)
                errors[$"items[{index}].discountedPrice"] = ["The sale price must be below the regular price."];
        }

        if (errors.Count > 0)
            return new(null, SaleFailure.Validation, errors);

        var sale = new SaleEvent
        {
            TenantId = tenant.Id,
            Id = Guid.NewGuid(),
            Name = request.Name!.Trim(),
            StartsAt = request.StartsAt.ToUniversalTime(),
            EndsAt = request.EndsAt.ToUniversalTime()
        };

        var items = requestedItems.Select(item => new SaleEventItem
        {
            TenantId = tenant.Id,
            SaleEventId = sale.Id,
            Sku = bySku[item!.Sku!.Trim()].Sku,
            DiscountedPrice = item.DiscountedPrice,
            InitialQuantity = item.InitialQuantity,
            RemainingQuantity = item.InitialQuantity
        }).ToArray();

        await store.AddAsync(sale, items, cancellationToken);
        return new(ToResponse(new(sale, items), tenant.CurrencyCode));
    }

    public async Task<SaleResult<SaleEventResponse>> GetAsync(
        string tenantId, Guid saleEventId, CancellationToken cancellationToken)
    {
        if (!ValidTenantId(tenantId))
            return new(null, SaleFailure.TenantNotFound);

        var tenant = await store.FindTenantAsync(tenantId, cancellationToken);
        if (tenant is null)
            return new(null, SaleFailure.TenantNotFound);

        var sale = await store.FindAsync(tenant.Id, saleEventId, cancellationToken);
        return sale is null
            ? new(null, SaleFailure.SaleNotFound)
            : new(ToResponse(sale, tenant.CurrencyCode));
    }

    public async Task<SaleResult<SalePageResponse>> ListAsync(
        string tenantId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!ValidTenantId(tenantId))
            return new(null, SaleFailure.TenantNotFound);

        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return new(null, SaleFailure.Validation,
                new() { ["pagination"] = ["Use page >= 1 and pageSize between 1 and 100 within the supported offset range."] });

        var tenant = await store.FindTenantAsync(tenantId, cancellationToken);
        if (tenant is null)
            return new(null, SaleFailure.TenantNotFound);

        var data = await store.ListAsync(tenant.Id, page, pageSize, cancellationToken);
        return new(new SalePageResponse(
            data.Items.Select(sale => ToResponse(sale, tenant.CurrencyCode)).ToArray(),
            page, pageSize, data.TotalCount));
    }

    private SaleEventResponse ToResponse(SaleEventData data, string currencyCode)
    {
        var now = timeProvider.GetUtcNow();
        var sale = data.Event;
        var status = now < sale.StartsAt ? "Upcoming" : now >= sale.EndsAt ? "Expired" : "Active";

        return new(sale.Id, sale.TenantId, currencyCode, sale.Name,
            sale.StartsAt, sale.EndsAt, status,
            data.Items.OrderBy(item => item.Sku, StringComparer.Ordinal)
                .Select(item => new SaleItemResponse(item.Sku, item.DiscountedPrice,
                    item.InitialQuantity, item.RemainingQuantity)).ToArray());
    }

    private static bool ValidTenantId(string tenantId) =>
        !string.IsNullOrWhiteSpace(tenantId) && tenantId.Length <= 64;

    private static Dictionary<string, string[]> Validate(string tenantId, CreateSaleEventRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!ValidTenantId(tenantId))
            errors["tenantId"] = ["A tenant identifier of up to 64 characters is required."];
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            errors["name"] = ["A name of up to 200 characters is required."];
        if (request.StartsAt == default || request.EndsAt == default || request.EndsAt <= request.StartsAt)
            errors["endsAt"] = ["Provide a start time and an end time strictly after it."];
        if (request.Items is null || request.Items.Count is < 1 or > 100)
        {
            errors["items"] = ["Provide between 1 and 100 sale items."];
            return errors;
        }

        var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            if (item is null)
            {
                errors[$"items[{index}]"] = ["An item cannot be null."];
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.Sku) || item.Sku.Trim().Length > 64)
                errors[$"items[{index}].sku"] = ["A SKU of up to 64 characters is required."];
            else if (!skus.Add(item.Sku.Trim()))
                errors[$"items[{index}].sku"] = ["Each SKU may appear only once in a sale."];
            if (item.InitialQuantity <= 0)
                errors[$"items[{index}].initialQuantity"] = ["Initial quantity must be positive."];
            if (item.DiscountedPrice <= 0 || item.DiscountedPrice > MaximumPrice ||
                decimal.Round(item.DiscountedPrice, 2) != item.DiscountedPrice)
                errors[$"items[{index}].discountedPrice"] = ["Provide a positive decimal(18,2) price with at most two decimal places."];
        }
        return errors;
    }
}
