namespace Sale.Application.Reservations;

public sealed record CreateReservationRequest(string? Sku, int Quantity);

public sealed record ReservationResponse(
    Guid Id, string TenantId, Guid SaleEventId, string Sku, int Quantity,
    decimal UnitPrice, string CurrencyCode, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? ConfirmedAt,
    bool CanConfirm);

public enum ReservationFailure
{
    Validation, NotFound, SaleInactive, CapacityUnavailable,
    IdempotencyConflict, Expired, Busy
}

public sealed record ReservationResult(
    ReservationResponse? Value = null, ReservationFailure? Failure = null,
    Dictionary<string, string[]>? Errors = null, bool Replayed = false);

public interface IReservationStore
{
    Task<ReservationResult> CreateAsync(string tenantId, Guid saleEventId,
        string idempotencyKey, string sku, int quantity, CancellationToken cancellationToken);
    Task<ReservationResult> GetAsync(string tenantId, Guid reservationId, CancellationToken cancellationToken);
    Task<ReservationResult> ConfirmAsync(string tenantId, Guid reservationId, CancellationToken cancellationToken);
}
