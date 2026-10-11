namespace Sale.Infrastructure.Reservations;

public sealed class ReservationOptions
{
    public int HoldSeconds { get; set; } = 600;
    public int ExpirationPollSeconds { get; set; } = 5;
    public int ExpirationBatchSize { get; set; } = 100;
}
