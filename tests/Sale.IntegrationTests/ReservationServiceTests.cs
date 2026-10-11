using Sale.Application.Reservations;
using Xunit;

namespace Sale.IntegrationTests;

[Trait("Category", "Unit")]
public sealed class ReservationServiceTests
{
    [Theory]
    [InlineData(null, "SHARED", 1, "Idempotency-Key")]
    [InlineData("", "SHARED", 1, "Idempotency-Key")]
    [InlineData("key with space", "SHARED", 1, "Idempotency-Key")]
    [InlineData("key", "", 1, "sku")]
    [InlineData("key", "SHARED", 0, "quantity")]
    [InlineData("key", "SHARED", -1, "quantity")]
    public async Task Invalid_requests_do_not_touch_the_database(string? key, string sku, int quantity, string field)
    {
        var store = new RecordingStore();
        var result = await new ReservationService(store).CreateAsync("tenant-a", Guid.NewGuid(), key,
            new(sku, quantity), TestContext.Current.CancellationToken);
        Assert.Equal(ReservationFailure.Validation, result.Failure);
        Assert.Contains(field, result.Errors!.Keys);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Valid_request_trims_sku_and_preserves_case_sensitive_key()
    {
        var store = new RecordingStore();
        await new ReservationService(store).CreateAsync("tenant-a", Guid.NewGuid(), "Checkout-A:1",
            new(" SHARED ", 2), TestContext.Current.CancellationToken);
        Assert.Equal(1, store.Calls);
        Assert.Equal("SHARED", store.Sku);
        Assert.Equal("Checkout-A:1", store.Key);
    }

    [Fact]
    public async Task Oversized_key_is_rejected_before_storage()
    {
        var store = new RecordingStore();
        var result = await new ReservationService(store).CreateAsync("tenant-a", Guid.NewGuid(), new string('x', 129),
            new("SHARED", 1), TestContext.Current.CancellationToken);
        Assert.Equal(ReservationFailure.Validation, result.Failure);
        Assert.Equal(0, store.Calls);
    }

    private sealed class RecordingStore : IReservationStore
    {
        public int Calls { get; private set; }
        public string? Sku { get; private set; }
        public string? Key { get; private set; }
        public Task<ReservationResult> CreateAsync(string tenantId, Guid saleEventId, string idempotencyKey,
            string sku, int quantity, CancellationToken cancellationToken)
        {
            Calls++; Sku = sku; Key = idempotencyKey;
            return Task.FromResult(new ReservationResult());
        }
        public Task<ReservationResult> GetAsync(string tenantId, Guid reservationId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<ReservationResult> ConfirmAsync(string tenantId, Guid reservationId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
