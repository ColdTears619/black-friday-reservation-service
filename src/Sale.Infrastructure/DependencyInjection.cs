using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sale.Infrastructure.Persistence;
using Sale.Application.Sales;
using Sale.Application.Reservations;
using Sale.Infrastructure.Reservations;

namespace Sale.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:SqlServer is not configured.");
        }

        services.AddDbContext<SaleDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ISaleEventStore, SaleEventStore>();
        services.AddScoped<SaleEventService>();
        services.AddScoped<DatabaseInitializer>();
        services.AddOptions<ReservationOptions>()
            .Configure(options =>
            {
                options.HoldSeconds = configuration.GetValue("Reservations:HoldSeconds", 600);
                options.ExpirationPollSeconds = configuration.GetValue("Reservations:ExpirationPollSeconds", 5);
                options.ExpirationBatchSize = configuration.GetValue("Reservations:ExpirationBatchSize", 100);
            })
            .Validate(options => options.HoldSeconds is >= 1 and <= 86400,
                "Reservation hold must be between 1 and 86400 seconds.")
            .Validate(options => options.ExpirationPollSeconds is >= 1 and <= 60,
                "Expiration poll interval must be between 1 and 60 seconds.")
            .Validate(options => options.ExpirationBatchSize is >= 1 and <= 1000,
                "Expiration batch size must be between 1 and 1000.")
            .ValidateOnStart();
        services.AddScoped<ReservationStore>();
        services.AddScoped<IReservationStore>(provider => provider.GetRequiredService<ReservationStore>());
        services.AddScoped<ReservationService>();

        return services;
    }

    public static IServiceCollection AddReservationExpirationWorker(this IServiceCollection services)
    {
        services.AddHostedService<ReservationExpirationWorker>();
        return services;
    }
}
