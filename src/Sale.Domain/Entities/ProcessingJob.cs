namespace Sale.Domain.Entities;

// Durable handoff only. Execution, leases and retry policy belong to the next phase.
public sealed class ProcessingJob
{
    public required string TenantId { get; init; }
    public required Guid Id { get; init; }
    public required Guid ReservationId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
