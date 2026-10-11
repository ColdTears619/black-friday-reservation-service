namespace Sale.Domain.Entities;

public sealed class Tenant
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string CurrencyCode { get; init; }
}
