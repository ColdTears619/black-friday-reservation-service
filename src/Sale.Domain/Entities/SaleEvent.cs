namespace Sale.Domain.Entities;

public sealed class SaleEvent
{
    public required string TenantId { get; init; }

    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset StartsAt { get; init; }

    public required DateTimeOffset EndsAt { get; init; }
}
