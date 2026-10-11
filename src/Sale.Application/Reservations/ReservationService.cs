namespace Sale.Application.Reservations;

public sealed class ReservationService(IReservationStore store)
{
    public Task<ReservationResult> CreateAsync(string tenantId, Guid saleEventId,
        string? idempotencyKey, CreateReservationRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!ValidTenant(tenantId)) errors["tenantId"] = ["Use a tenant ID of 1 to 64 characters."];
        if (saleEventId == Guid.Empty) errors["saleEventId"] = ["A sale event ID is required."];
        if (string.IsNullOrWhiteSpace(request.Sku) || request.Sku.Trim().Length > 64)
            errors["sku"] = ["Use a SKU of 1 to 64 characters."];
        if (request.Quantity <= 0) errors["quantity"] = ["Quantity must be positive."];
        if (string.IsNullOrEmpty(idempotencyKey) || idempotencyKey.Length > 128 ||
            idempotencyKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.' and not ':'))
            errors["Idempotency-Key"] = ["Use 1 to 128 ASCII letters, digits, hyphens, underscores, dots or colons."];

        return errors.Count > 0
            ? Task.FromResult(new ReservationResult(Failure: ReservationFailure.Validation, Errors: errors))
            : store.CreateAsync(tenantId, saleEventId, idempotencyKey!, request.Sku!.Trim(), request.Quantity, cancellationToken);
    }

    public Task<ReservationResult> GetAsync(string tenantId, Guid reservationId, CancellationToken cancellationToken) =>
        ValidTenant(tenantId) && reservationId != Guid.Empty
            ? store.GetAsync(tenantId, reservationId, cancellationToken)
            : Task.FromResult(new ReservationResult(Failure: ReservationFailure.NotFound));

    public Task<ReservationResult> ConfirmAsync(string tenantId, Guid reservationId, CancellationToken cancellationToken) =>
        ValidTenant(tenantId) && reservationId != Guid.Empty
            ? store.ConfirmAsync(tenantId, reservationId, cancellationToken)
            : Task.FromResult(new ReservationResult(Failure: ReservationFailure.NotFound));

    private static bool ValidTenant(string tenantId) => !string.IsNullOrWhiteSpace(tenantId) && tenantId.Length <= 64;
}
