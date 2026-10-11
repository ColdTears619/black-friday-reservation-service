using Microsoft.AspNetCore.Mvc;
using Sale.Application.Reservations;

namespace Sale.Api.Endpoints;

public static class ReservationEndpoints
{
    public static IEndpointRouteBuilder MapReservationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tenants/{tenantId}").WithTags("Reservations");
        group.MapPost("/sales/{saleEventId:guid}/reservations", async (string tenantId, Guid saleEventId,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CreateReservationRequest request, ReservationService service, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(tenantId, saleEventId, idempotencyKey, request, cancellationToken);
            if (result.Failure is not null) return Failure(result, context);
            context.Response.Headers["Idempotency-Replayed"] = result.Replayed ? "true" : "false";
            return result.Replayed ? Results.Ok(result.Value) : Results.CreatedAtRoute("GetReservation",
                new { tenantId = result.Value!.TenantId, reservationId = result.Value.Id }, result.Value);
        })
        .WithName("CreateReservation").WithSummary("Hold one SKU temporarily after checkout review")
        .Produces<ReservationResponse>(201).Produces<ReservationResponse>(200)
        .ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);

        group.MapGet("/reservations/{reservationId:guid}", async (string tenantId, Guid reservationId,
            ReservationService service, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(tenantId, reservationId, cancellationToken);
            return result.Failure is null ? Results.Ok(result.Value) : Failure(result, context);
        }).WithName("GetReservation").Produces<ReservationResponse>().ProducesProblem(404);

        group.MapPost("/reservations/{reservationId:guid}/confirm", async (string tenantId, Guid reservationId,
            ReservationService service, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await service.ConfirmAsync(tenantId, reservationId, cancellationToken);
            if (result.Failure is not null) return Failure(result, context);
            return Results.Accepted($"/api/tenants/{Uri.EscapeDataString(result.Value!.TenantId)}/reservations/{reservationId}", result.Value);
        })
        .WithName("ConfirmReservation")
        .WithSummary("Simulate purchase confirmation; no real payment is performed")
        .Produces<ReservationResponse>(202).ProducesProblem(404).ProducesProblem(410).ProducesProblem(503);
        return endpoints;
    }

    private static IResult Failure(ReservationResult result, HttpContext context)
    {
        if (result.Failure == ReservationFailure.Validation) return Results.ValidationProblem(result.Errors!);
        var (status, title, detail) = result.Failure switch
        {
            ReservationFailure.NotFound => (404, "Resource not found.", "The requested resource does not exist in this tenant."),
            ReservationFailure.SaleInactive => (409, "Sale is not active.", "New reservations are accepted only during the sale window."),
            ReservationFailure.CapacityUnavailable => (409, "No reservable capacity.", "The requested quantity is unavailable. Units may be held by other customers or already confirmed. Check availability again later; no waiting-list position is created."),
            ReservationFailure.IdempotencyConflict => (409, "Idempotency key conflict.", "This key was already used for a different sale, SKU or quantity."),
            ReservationFailure.Expired => (410, "Reservation expired.", "This hold can no longer be confirmed. Create a new reservation with a new key if the sale is still active and capacity is available."),
            ReservationFailure.Busy => (503, "Reservation service is busy.", "Retry the same request. For reservation creation, reuse the same Idempotency-Key."),
            _ => throw new InvalidOperationException("Unexpected reservation result.")
        };
        if (result.Failure == ReservationFailure.Busy) context.Response.Headers.RetryAfter = "1";
        return Results.Problem(statusCode: status, title: title, detail: detail);
    }
}
