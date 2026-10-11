using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sale.Domain.Entities;

namespace Sale.Infrastructure.Persistence.Configurations;

public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations", table =>
        {
            table.HasCheckConstraint("CK_Reservations_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_Reservations_UnitPrice", "[UnitPrice] > 0");
            table.HasCheckConstraint("CK_Reservations_Expiry", "[ExpiresAt] > [CreatedAt]");
            table.HasCheckConstraint("CK_Reservations_Status",
                "([Status] IN ('Pending', 'Expired') AND [ConfirmedAt] IS NULL) OR " +
                "([Status] = 'Confirmed' AND [ConfirmedAt] >= [CreatedAt] AND [ConfirmedAt] < [ExpiresAt] AND [ConfirmedAt] IS NOT NULL)");
        });
        builder.HasKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.TenantId).HasMaxLength(64);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Sku).HasMaxLength(64);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });
        builder.HasOne<SaleEventItem>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SaleEventId, x.Sku }).OnDelete(DeleteBehavior.Restrict);
    }
}
