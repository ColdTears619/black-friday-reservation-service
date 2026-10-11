using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sale.Application.Reservations;
using Sale.Domain.Entities;
using Sale.Infrastructure.Persistence;
using Sale.Infrastructure.Reservations;
using Xunit;

namespace Sale.IntegrationTests;

[Trait("Category", "Database")]
public sealed class ReservationConcurrencyTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_hundred_concurrent_requests_for_thirty_units_never_oversell()
    {
        var saleId = await CreateSaleAsync(30);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 100).Select(async _ =>
        {
            await gate.Task;
            return await ReserveAsync(saleId);
        }).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(30, results.Count(x => x.Failure is null));
        Assert.Equal(70, results.Count(x => x.Failure == ReservationFailure.CapacityUnavailable));
        await using var db = database.CreateContext();
        Assert.Equal(0, await RemainingAsync(db, saleId));
        Assert.Equal(30, await db.Reservations.CountAsync(x => x.SaleEventId == saleId, Token));
        Assert.Equal(30, await db.Reservations.Where(x => x.SaleEventId == saleId).SumAsync(x => x.Quantity, Token));
        Assert.Equal(0, await db.ProcessingJobs.Join(db.Reservations, j => new { j.TenantId, Id = j.ReservationId },
            r => new { r.TenantId, r.Id }, (j, r) => r).CountAsync(r => r.SaleEventId == saleId, Token));
    }

    [Fact]
    public async Task Concurrent_replays_create_one_hold_without_extending_its_deadline()
    {
        var saleId = await CreateSaleAsync(30);
        var key = Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ReserveAsync(saleId, key)));
        Assert.All(results, result => Assert.Null(result.Failure));
        Assert.Single(results, x => !x.Replayed);
        Assert.Single(results.Select(x => x.Value!.Id).Distinct());
        Assert.Single(results.Select(x => x.Value!.ExpiresAt).Distinct());
        Assert.All(results, x => Assert.Equal(TimeSpan.FromMinutes(10), x.Value!.ExpiresAt - x.Value.CreatedAt));
        await using var db = database.CreateContext();
        Assert.Equal(29, await RemainingAsync(db, saleId));
        Assert.Equal(1, await db.Reservations.CountAsync(x => x.SaleEventId == saleId, Token));
    }

    [Fact]
    public async Task Same_key_with_different_quantity_conflicts_without_changing_stock()
    {
        var saleId = await CreateSaleAsync(30);
        var key = Guid.NewGuid().ToString("N");
        var first = await ReserveAsync(saleId, key, 2);
        var second = await ReserveAsync(saleId, key, 3);
        Assert.Null(first.Failure);
        Assert.Equal(ReservationFailure.IdempotencyConflict, second.Failure);
        await using var db = database.CreateContext();
        Assert.Equal(28, await RemainingAsync(db, saleId));
    }

    [Fact]
    public async Task Same_key_is_independent_across_tenants_and_reservations_are_not_visible_across_tenants()
    {
        var firstSale = await CreateSaleAsync(10);
        var secondSale = await CreateSaleAsync(10, "tenant-b");
        var key = Guid.NewGuid().ToString("N");
        var first = await ReserveAsync(firstSale, key);
        var second = await ReserveAsync(secondSale, key, tenant: "tenant-b");
        Assert.Null(first.Failure);
        Assert.Null(second.Failure);
        Assert.NotEqual(first.Value!.Id, second.Value!.Id);
        await using var db = database.CreateContext();
        var store = Store(db);
        Assert.Equal(ReservationFailure.NotFound, (await store.GetAsync("tenant-b", first.Value.Id, Token)).Failure);
        Assert.Equal(ReservationFailure.NotFound, (await store.ConfirmAsync("tenant-b", first.Value.Id, Token)).Failure);
        Assert.Equal(ReservationStatus.Pending, (await db.Reservations.SingleAsync(x => x.Id == first.Value.Id, Token)).Status);
    }

    [Fact]
    public async Task Key_case_is_significant_but_tenant_case_uses_the_same_idempotency_scope()
    {
        var saleId = await CreateSaleAsync(10);
        var key = Guid.NewGuid().ToString("N");
        var first = await ReserveAsync(saleId, key + "a");
        var replay = await ReserveAsync(saleId, key + "a", tenant: "TENANT-A");
        var distinct = await ReserveAsync(saleId, key + "A");
        Assert.Equal(first.Value!.Id, replay.Value!.Id);
        Assert.True(replay.Replayed);
        Assert.NotEqual(first.Value.Id, distinct.Value!.Id);
        await using var db = database.CreateContext();
        Assert.Equal(8, await RemainingAsync(db, saleId));
    }

    [Fact]
    public async Task Concurrent_confirmation_creates_one_job_and_does_not_decrement_stock_again()
    {
        var saleId = await CreateSaleAsync(30);
        var hold = (await ReserveAsync(saleId)).Value!;
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = database.CreateContext();
            return await Store(db).ConfirmAsync("tenant-a", hold.Id, Token);
        }));
        Assert.All(results, result =>
        {
            Assert.Null(result.Failure);
            Assert.Equal("Confirmed", result.Value!.Status);
        });
        await using var check = database.CreateContext();
        Assert.Equal(29, await RemainingAsync(check, saleId));
        Assert.Equal(1, await check.ProcessingJobs.CountAsync(x => x.ReservationId == hold.Id, Token));
    }

    [Fact]
    public async Task Hold_can_be_confirmed_after_sale_end_before_its_own_deadline()
    {
        var saleId = await CreateSaleAsync(10);
        var hold = (await ReserveAsync(saleId)).Value!;
        await EndSaleAsync(saleId);
        await using var db = database.CreateContext();
        var confirmed = await Store(db).ConfirmAsync("tenant-a", hold.Id, Token);
        Assert.Null(confirmed.Failure);
        Assert.Equal("Confirmed", confirmed.Value!.Status);
        Assert.Equal(ReservationFailure.SaleInactive, (await ReserveAsync(saleId)).Failure);
    }

    [Fact]
    public async Task Competing_sweepers_and_late_confirmation_release_exactly_once_and_create_no_job()
    {
        var saleId = await CreateSaleAsync(30);
        var hold = (await ReserveAsync(saleId, quantity: 3)).Value!;
        await MakeExpiredAsync(hold.Id);
        var tasks = Enumerable.Range(0, 8).Select(async index =>
        {
            await using var db = database.CreateContext();
            if (index % 2 == 0) await Store(db).ExpireBatchAsync(Token);
            else Assert.Equal(ReservationFailure.Expired, (await Store(db).ConfirmAsync("tenant-a", hold.Id, Token)).Failure);
        });
        await Task.WhenAll(tasks);
        await using var check = database.CreateContext();
        Assert.Equal(30, await RemainingAsync(check, saleId));
        Assert.Equal(ReservationStatus.Expired, (await check.Reservations.SingleAsync(x => x.Id == hold.Id, Token)).Status);
        Assert.False(await check.ProcessingJobs.AnyAsync(x => x.ReservationId == hold.Id, Token));
        Assert.Null((await ReserveAsync(saleId, quantity: 30)).Failure);
    }

    [Fact]
    public async Task Expired_replay_does_not_create_a_new_hold_or_extend_the_deadline()
    {
        var saleId = await CreateSaleAsync(10);
        var key = Guid.NewGuid().ToString("N");
        var hold = (await ReserveAsync(saleId, key)).Value!;
        await MakeExpiredAsync(hold.Id);
        await using var db = database.CreateContext();
        await Store(db).ExpireBatchAsync(Token);
        var saved = await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == hold.Id, Token);
        var replay = await ReserveAsync(saleId, key);
        Assert.True(replay.Replayed);
        Assert.Equal(hold.Id, replay.Value!.Id);
        Assert.Equal("Expired", replay.Value.Status);
        Assert.False(replay.Value.CanConfirm);
        Assert.Equal(saved.ExpiresAt, replay.Value.ExpiresAt);
        Assert.Equal(10, await RemainingAsync(db, saleId));
    }

    [Fact]
    public async Task Late_confirmation_releases_stock_even_when_expiration_worker_is_offline()
    {
        var saleId = await CreateSaleAsync(10);
        var hold = (await ReserveAsync(saleId)).Value!;
        await MakeExpiredAsync(hold.Id);
        await using var db = database.CreateContext();
        var pending = await Store(db).GetAsync("tenant-a", hold.Id, Token);
        Assert.False(pending.Value!.CanConfirm);
        Assert.Equal(ReservationFailure.Expired, (await Store(db).ConfirmAsync("tenant-a", hold.Id, Token)).Failure);
        Assert.Equal(10, await RemainingAsync(db, saleId));
    }

    [Fact]
    public async Task Expiration_after_sale_end_releases_capacity_but_does_not_reopen_sale()
    {
        var saleId = await CreateSaleAsync(10);
        var hold = (await ReserveAsync(saleId)).Value!;
        await EndSaleAsync(saleId);
        await MakeExpiredAsync(hold.Id);
        await using var db = database.CreateContext();
        await Store(db).ExpireBatchAsync(Token);
        Assert.Equal(10, await RemainingAsync(db, saleId));
        Assert.Equal(ReservationFailure.SaleInactive, (await ReserveAsync(saleId)).Failure);
    }

    [Fact]
    public async Task Upcoming_sale_rejects_reservations_without_changing_stock()
    {
        var saleId = await CreateSaleAsync(10, startsInMinutes: 60);
        Assert.Equal(ReservationFailure.SaleInactive, (await ReserveAsync(saleId)).Failure);
        await using var db = database.CreateContext();
        Assert.Equal(10, await RemainingAsync(db, saleId));
    }

    [Fact]
    public async Task Reservation_insert_failure_rolls_back_the_stock_decrement()
    {
        var saleId = await CreateSaleAsync(10);
        await using (var db = database.CreateContext(new FailInsert<Reservation>()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(db).CreateAsync("tenant-a", saleId,
                Guid.NewGuid().ToString("N"), "SHARED", 1, Token));
        await using var check = database.CreateContext();
        Assert.Equal(10, await RemainingAsync(check, saleId));
        Assert.False(await check.Reservations.AnyAsync(x => x.SaleEventId == saleId, Token));
    }

    [Fact]
    public async Task Job_insert_failure_rolls_back_confirmation_and_allows_a_safe_retry()
    {
        var saleId = await CreateSaleAsync(10);
        var hold = (await ReserveAsync(saleId)).Value!;
        await using (var db = database.CreateContext(new FailInsert<ProcessingJob>()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(db).ConfirmAsync("tenant-a", hold.Id, Token));
        await using var check = database.CreateContext();
        Assert.Equal(ReservationStatus.Pending, (await check.Reservations.AsNoTracking().SingleAsync(x => x.Id == hold.Id, Token)).Status);
        Assert.False(await check.ProcessingJobs.AnyAsync(x => x.ReservationId == hold.Id, Token));
        Assert.Equal(9, await RemainingAsync(check, saleId));
        Assert.Null((await Store(check).ConfirmAsync("tenant-a", hold.Id, Token)).Failure);
        Assert.Equal(1, await check.ProcessingJobs.CountAsync(x => x.ReservationId == hold.Id, Token));
    }

    private ReservationStore Store(SaleDbContext db) => new(db, Options.Create(new ReservationOptions()),
        NullLogger<ReservationStore>.Instance);

    [Fact]
    public async Task Confirmed_purchase_is_not_released_when_its_original_hold_deadline_passes()
    {
        var saleId = await CreateSaleAsync(10);
        var hold = (await ReserveAsync(saleId)).Value!;
        await using var db = database.CreateContext();
        Assert.Null((await Store(db).ConfirmAsync("tenant-a", hold.Id, Token)).Failure);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [Reservations]
            SET [CreatedAt] = DATEADD(minute, -20, SYSUTCDATETIME()),
                [ConfirmedAt] = DATEADD(minute, -15, SYSUTCDATETIME()),
                [ExpiresAt] = DATEADD(minute, -10, SYSUTCDATETIME())
            WHERE [TenantId] = N'tenant-a' AND [Id] = {hold.Id};
            """, Token);
        await Store(db).ExpireBatchAsync(Token);
        Assert.Equal(9, await RemainingAsync(db, saleId));
        var replay = await Store(db).ConfirmAsync("tenant-a", hold.Id, Token);
        Assert.Null(replay.Failure);
        Assert.Equal("Confirmed", replay.Value!.Status);
        Assert.Equal(1, await db.ProcessingJobs.CountAsync(x => x.ReservationId == hold.Id, Token));
    }

    [Fact]
    public async Task Expiration_background_service_recovers_after_a_failed_sweep()
    {
        var saleId = await CreateSaleAsync(10);
        var hold = (await ReserveAsync(saleId)).Value!;
        await MakeExpiredAsync(hold.Id);
        var attempts = 0;
        var options = Options.Create(new ReservationOptions { ExpirationPollSeconds = 1 });
        var services = new ServiceCollection();
        services.AddScoped(_ => database.CreateContext());
        services.AddScoped(provider =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new InvalidOperationException("Simulated temporary failure.");
            return Store(provider.GetRequiredService<SaleDbContext>());
        });
        await using var provider = services.BuildServiceProvider();
        using var worker = new ReservationExpirationWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            options, NullLogger<ReservationExpirationWorker>.Instance);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await worker.StartAsync(timeout.Token);
        try
        {
            while (true)
            {
                await using var check = database.CreateContext();
                if (await check.Reservations.AnyAsync(x => x.Id == hold.Id && x.Status == ReservationStatus.Expired, timeout.Token)) break;
                await Task.Delay(100, timeout.Token);
            }
        }
        finally { await worker.StopAsync(CancellationToken.None); }
        Assert.True(attempts >= 2);
        await using var finalCheck = database.CreateContext();
        Assert.Equal(10, await RemainingAsync(finalCheck, saleId));
    }

    private async Task<ReservationResult> ReserveAsync(Guid saleId, string? key = null, int quantity = 1, string tenant = "tenant-a")
    {
        await using var db = database.CreateContext();
        return await Store(db).CreateAsync(tenant, saleId, key ?? Guid.NewGuid().ToString("N"), "SHARED", quantity, Token);
    }

    private async Task<Guid> CreateSaleAsync(int quantity, string tenant = "tenant-a", int startsInMinutes = -60)
    {
        await using var db = database.CreateContext();
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]").SingleAsync(Token);
        var id = Guid.NewGuid();
        db.SaleEvents.Add(new SaleEvent { TenantId = tenant, Id = id, Name = "Reservation test", StartsAt = now.AddMinutes(startsInMinutes), EndsAt = now.AddHours(2) });
        db.SaleEventItems.Add(new SaleEventItem { TenantId = tenant, SaleEventId = id, Sku = "SHARED", DiscountedPrice = 500, InitialQuantity = quantity, RemainingQuantity = quantity });
        await db.SaveChangesAsync(Token);
        return id;
    }

    private async Task MakeExpiredAsync(Guid reservationId)
    {
        await using var db = database.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [Reservations]
            SET [CreatedAt] = DATEADD(minute, -20, SYSUTCDATETIME()),
                [ExpiresAt] = DATEADD(minute, -10, SYSUTCDATETIME())
            WHERE [TenantId] = N'tenant-a' AND [Id] = {reservationId};
            """, Token);
    }

    private async Task EndSaleAsync(Guid saleId)
    {
        await using var db = database.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [SaleEvents] SET [StartsAt] = DATEADD(day, -2, SYSUTCDATETIME()),
                [EndsAt] = DATEADD(day, -1, SYSUTCDATETIME())
            WHERE [TenantId] = N'tenant-a' AND [Id] = {saleId};
            """, Token);
    }

    private Task<int> RemainingAsync(SaleDbContext db, Guid saleId) => db.SaleEventItems
        .Where(x => x.TenantId == "tenant-a" && x.SaleEventId == saleId && x.Sku == "SHARED")
        .Select(x => x.RemainingQuantity).SingleAsync(Token);

    private sealed class FailInsert<TEntity> : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries().Any(x => x.State == EntityState.Added && x.Entity is TEntity))
                throw new InvalidOperationException("Simulated insert failure.");
            return ValueTask.FromResult(result);
        }
    }
}
