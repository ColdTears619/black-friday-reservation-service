namespace Sale.Domain.Entities;

public sealed class SaleEventItem
{
    public required string TenantId { get; init; }

    public required Guid SaleEventId { get; init; }

    public required string Sku { get; init; }

    public required decimal DiscountedPrice { get; init; }

    public required int InitialQuantity { get; init; }

    public required int RemainingQuantity { get; init; }
}
