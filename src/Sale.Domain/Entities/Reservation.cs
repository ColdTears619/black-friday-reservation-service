namespace Sale.Domain.Entities;

public enum ReservationStatus { Pending, Confirmed, Expired }

public sealed class Reservation
{
    public required string TenantId { get; init; }
    public required Guid Id { get; init; }
    public required Guid SaleEventId { get; init; }
    public required string Sku { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required string CurrencyCode { get; init; }
    public required string IdempotencyKey { get; init; }
    public required ReservationStatus Status { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}
