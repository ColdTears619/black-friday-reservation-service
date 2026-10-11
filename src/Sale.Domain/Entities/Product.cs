namespace Sale.Domain.Entities;

public sealed class Product
{
    public required string TenantId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required decimal RegularPrice { get; init; }
}
