using Sale.Application.Sales;

namespace Sale.Api.Endpoints;

public static class SaleEventEndpoints
{
    public static IEndpointRouteBuilder MapSaleEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tenants/{tenantId}/sales").WithTags("Sale Events");

        group.MapPost("", async (string tenantId, CreateSaleEventRequest request,
            SaleEventService service, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(tenantId, request, cancellationToken);
            if (result.Failure is not null)
                return Failure(result);

            var sale = result.Value!;
            loggerFactory.CreateLogger("Sale.Api.SaleEvents").LogInformation(
                new EventId(1001, "SaleCreated"),
                "Sale {SaleEventId} created for tenant {TenantId} with {ItemCount} items",
                sale.Id, sale.TenantId, sale.Items.Count);

            return Results.CreatedAtRoute("GetSaleEvent",
                new { tenantId = sale.TenantId, saleEventId = sale.Id }, sale);
        })
        .WithName("CreateSaleEvent")
        .Produces<SaleEventResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{saleEventId:guid}", async (string tenantId, Guid saleEventId,
            SaleEventService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(tenantId, saleEventId, cancellationToken);
            return result.Failure is null ? Results.Ok(result.Value) : Failure(result);
        })
        .WithName("GetSaleEvent")
        .Produces<SaleEventResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("", async (string tenantId, int? page, int? pageSize,
            SaleEventService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(tenantId, page ?? 1, pageSize ?? 20, cancellationToken);
            return result.Failure is null ? Results.Ok(result.Value) : Failure(result);
        })
        .WithName("ListSaleEvents")
        .Produces<SalePageResponse>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static IResult Failure<T>(SaleResult<T> result) where T : class => result.Failure switch
    {
        SaleFailure.Validation => Results.ValidationProblem(result.Errors!),
        SaleFailure.TenantNotFound => Results.Problem(
            statusCode: StatusCodes.Status404NotFound, title: "Tenant not found."),
        SaleFailure.SaleNotFound => Results.Problem(
            statusCode: StatusCodes.Status404NotFound, title: "Sale event not found."),
        _ => throw new InvalidOperationException("Unexpected sale operation result.")
    };
}
