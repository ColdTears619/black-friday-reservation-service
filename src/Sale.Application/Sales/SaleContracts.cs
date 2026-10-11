using Sale.Domain.Entities;

namespace Sale.Application.Sales;

public sealed record CreateSaleEventRequest(
    string? Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<CreateSaleItemRequest?>? Items);

public sealed record CreateSaleItemRequest(
    string? Sku,
    decimal DiscountedPrice,
    int InitialQuantity);

public sealed record SaleItemResponse(
    string Sku,
    decimal DiscountedPrice,
    int InitialQuantity,
    int RemainingQuantity);

public sealed record SaleEventResponse(
    Guid Id,
    string TenantId,
    string CurrencyCode,
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    IReadOnlyList<SaleItemResponse> Items);

public sealed record SalePageResponse(
    IReadOnlyList<SaleEventResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public enum SaleFailure
{
    Validation,
    TenantNotFound,
    SaleNotFound
}

public sealed record SaleResult<T>(
    T? Value,
    SaleFailure? Failure = null,
    Dictionary<string, string[]>? Errors = null) where T : class;

public sealed record SaleEventData(
    SaleEvent Event,
    IReadOnlyList<SaleEventItem> Items);

public sealed record SalePageData(
    IReadOnlyList<SaleEventData> Items,
    int TotalCount);

public interface ISaleEventStore
{
    Task<Tenant?> FindTenantAsync(string tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> FindProductsAsync(
        string tenantId, IReadOnlyList<string> skus, CancellationToken cancellationToken);

    Task AddAsync(
        SaleEvent sale, IReadOnlyList<SaleEventItem> items, CancellationToken cancellationToken);

    Task<SaleEventData?> FindAsync(
        string tenantId, Guid saleEventId, CancellationToken cancellationToken);

    Task<SalePageData> ListAsync(
        string tenantId, int page, int pageSize, CancellationToken cancellationToken);
}
