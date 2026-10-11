using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sale.Application.Reservations;
using Sale.Domain.Entities;
using Sale.Infrastructure.Persistence;

namespace Sale.Infrastructure.Reservations;

public sealed class ReservationStore(
    SaleDbContext db, IOptions<ReservationOptions> options, ILogger<ReservationStore> logger)
    : IReservationStore
{
    public Task<ReservationResult> CreateAsync(string tenantId, Guid saleEventId,
        string idempotencyKey, string sku, int quantity, CancellationToken cancellationToken) =>
        HandleContentionAsync(() => CreateCoreAsync(tenantId, saleEventId, idempotencyKey, sku, quantity, cancellationToken));

    private async Task<ReservationResult> CreateCoreAsync(string tenantId, Guid saleEventId,
        string idempotencyKey, string sku, int quantity, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (tenant is null) return new(Failure: ReservationFailure.NotFound);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Canonical tenant spelling matters: application-lock resources are binary compared.
        var resource = "reservation:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{tenant.Id.Length}:{tenant.Id}:{idempotencyKey}")));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = {resource},
                @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
            IF @result < 0 THROW 51001, 'Reservation request is busy; retry with the same key.', 1;
            """, cancellationToken);

        var existing = await db.Reservations.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenant.Id && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.SaleEventId != saleEventId || existing.Quantity != quantity ||
                !string.Equals(existing.Sku, sku, StringComparison.OrdinalIgnoreCase))
                return new(Failure: ReservationFailure.IdempotencyConflict);
            return new(ToResponse(existing, await DatabaseNowAsync(cancellationToken)), Replayed: true);
        }

        // Lock this sale item before sampling time, including when a competing request made us wait.
        var item = await db.SaleEventItems.FromSqlInterpolated($"""
            SELECT * FROM [SaleEventItems] WITH (UPDLOCK)
            WHERE [TenantId] = {tenant.Id} AND [SaleEventId] = {saleEventId} AND [Sku] = {sku}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (item is null) return new(Failure: ReservationFailure.NotFound);

        var sale = await db.SaleEvents.AsNoTracking().SingleAsync(
            x => x.TenantId == tenant.Id && x.Id == saleEventId, cancellationToken);
        var now = await DatabaseNowAsync(cancellationToken);
        if (now < sale.StartsAt || now >= sale.EndsAt)
            return new(Failure: ReservationFailure.SaleInactive);

        var changed = await db.SaleEventItems
            .Where(x => x.TenantId == tenant.Id && x.SaleEventId == saleEventId &&
                x.Sku == item.Sku && x.RemainingQuantity >= quantity)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RemainingQuantity,
                x => x.RemainingQuantity - quantity), cancellationToken);
        if (changed == 0) return new(Failure: ReservationFailure.CapacityUnavailable);

        var reservation = new Reservation
        {
            TenantId = tenant.Id, Id = Guid.NewGuid(), SaleEventId = saleEventId,
            Sku = item.Sku, Quantity = quantity, UnitPrice = item.DiscountedPrice,
            CurrencyCode = tenant.CurrencyCode, IdempotencyKey = idempotencyKey,
            Status = ReservationStatus.Pending, CreatedAt = now,
            ExpiresAt = now.AddSeconds(options.Value.HoldSeconds)
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(new EventId(1100, "ReservationCreated"),
            "Reservation {ReservationId} held for tenant {TenantId}, sale {SaleEventId}, SKU {Sku}, quantity {Quantity}, until {ExpiresAt}",
            reservation.Id, tenant.Id, saleEventId, item.Sku, quantity, reservation.ExpiresAt);
        return new(ToResponse(reservation, now));
    }

    public async Task<ReservationResult> GetAsync(string tenantId, Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await db.Reservations.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == reservationId, cancellationToken);
        return reservation is null ? new(Failure: ReservationFailure.NotFound)
            : new(ToResponse(reservation, await DatabaseNowAsync(cancellationToken)));
    }

    public Task<ReservationResult> ConfirmAsync(string tenantId, Guid reservationId, CancellationToken cancellationToken) =>
        HandleContentionAsync(() => ConfirmCoreAsync(tenantId, reservationId, cancellationToken));

    private async Task<ReservationResult> ConfirmCoreAsync(string tenantId, Guid reservationId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reservation = await LockReservationAsync(tenantId, reservationId, cancellationToken);
        if (reservation is null) return new(Failure: ReservationFailure.NotFound);
        var now = await DatabaseNowAsync(cancellationToken);
        if (reservation.Status == ReservationStatus.Confirmed)
            return new(ToResponse(reservation, now), Replayed: true);
        if (reservation.Status == ReservationStatus.Expired)
            return new(ToResponse(reservation, now), ReservationFailure.Expired);
        if (now >= reservation.ExpiresAt)
        {
            await ReleaseAsync(reservation, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            LogExpired(reservation);
            return new(ToResponse(reservation, now), ReservationFailure.Expired);
        }

        await db.Reservations.Where(x => x.TenantId == reservation.TenantId && x.Id == reservation.Id)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, ReservationStatus.Confirmed)
                .SetProperty(x => x.ConfirmedAt, now), cancellationToken);
        var job = new ProcessingJob
        {
            TenantId = reservation.TenantId, Id = Guid.NewGuid(), ReservationId = reservation.Id,
            Status = "Pending", CreatedAt = now
        };
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        reservation.Status = ReservationStatus.Confirmed;
        reservation.ConfirmedAt = now;
        logger.LogInformation(new EventId(1101, "ReservationConfirmed"),
            "Reservation {ReservationId} confirmed for tenant {TenantId}; job {JobId} queued",
            reservation.Id, reservation.TenantId, job.Id);
        return new(ToResponse(reservation, now));
    }

    public async Task<int> ExpireBatchAsync(CancellationToken cancellationToken)
    {
        var now = await DatabaseNowAsync(cancellationToken);
        var candidates = await db.Reservations.AsNoTracking()
            .Where(x => x.Status == ReservationStatus.Pending && x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt).ThenBy(x => x.TenantId).ThenBy(x => x.Id)
            .Select(x => new { x.TenantId, x.Id })
            .Take(options.Value.ExpirationBatchSize).ToListAsync(cancellationToken);
        var expired = 0;
        foreach (var candidate in candidates)
        {
            // Small transactions; multiple sweepers may see a candidate, but only one releases it.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var reservation = await LockReservationAsync(candidate.TenantId, candidate.Id, cancellationToken);
            if (reservation is null || reservation.Status != ReservationStatus.Pending ||
                reservation.ExpiresAt > await DatabaseNowAsync(cancellationToken)) continue;
            await ReleaseAsync(reservation, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            LogExpired(reservation);
            expired++;
        }
        return expired;
    }

    private Task<Reservation?> LockReservationAsync(string tenantId, Guid reservationId, CancellationToken cancellationToken) =>
        db.Reservations.FromSqlInterpolated($"""
            SELECT * FROM [Reservations] WITH (UPDLOCK)
            WHERE [TenantId] = {tenantId} AND [Id] = {reservationId}
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    private async Task ReleaseAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        await db.Reservations.Where(x => x.TenantId == reservation.TenantId && x.Id == reservation.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, ReservationStatus.Expired), cancellationToken);
        var changed = await db.SaleEventItems.Where(x => x.TenantId == reservation.TenantId &&
                x.SaleEventId == reservation.SaleEventId && x.Sku == reservation.Sku &&
                x.RemainingQuantity <= x.InitialQuantity - reservation.Quantity)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RemainingQuantity,
                x => x.RemainingQuantity + reservation.Quantity), cancellationToken);
        if (changed != 1) throw new InvalidOperationException("Reservation stock invariant was violated during release.");
        reservation.Status = ReservationStatus.Expired;
    }

    private Task<DateTimeOffset> DatabaseNowAsync(CancellationToken cancellationToken) =>
        db.Database.SqlQuery<DateTimeOffset>($"SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]")
            .SingleAsync(cancellationToken);

    private static ReservationResponse ToResponse(Reservation reservation, DateTimeOffset now) => new(
        reservation.Id, reservation.TenantId, reservation.SaleEventId, reservation.Sku,
        reservation.Quantity, reservation.UnitPrice, reservation.CurrencyCode, reservation.Status.ToString(),
        reservation.CreatedAt, reservation.ExpiresAt, reservation.ConfirmedAt,
        reservation.Status == ReservationStatus.Pending && now < reservation.ExpiresAt);

    private void LogExpired(Reservation reservation) => logger.LogInformation(new EventId(1102, "ReservationExpired"),
        "Reservation {ReservationId} expired for tenant {TenantId}; released {Quantity} units of {Sku} in sale {SaleEventId}",
        reservation.Id, reservation.TenantId, reservation.Quantity, reservation.Sku, reservation.SaleEventId);

    private async Task<ReservationResult> HandleContentionAsync(Func<Task<ReservationResult>> operation)
    {
        try { return await operation(); }
        catch (SqlException exception) when (exception.Number is 1205 or 1222 or 51001 or -2)
        {
            logger.LogWarning(new EventId(1103, "ReservationBusy"), exception,
                "Reservation operation could not acquire database resources in time; retry the same request");
            return new(Failure: ReservationFailure.Busy);
        }
    }
}
