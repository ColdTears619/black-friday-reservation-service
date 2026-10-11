using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sale.Domain.Entities;

namespace Sale.Infrastructure.Persistence.Configurations;

public sealed class SaleEventConfiguration
    : IEntityTypeConfiguration<SaleEvent>
{
    public void Configure(EntityTypeBuilder<SaleEvent> builder)
    {
        builder.ToTable("SaleEvents", table =>
        {
            table.HasCheckConstraint(
                "CK_SaleEvents_TimeRange",
                "[EndsAt] > [StartsAt]");
        });

        builder.HasKey(x => new { x.TenantId, x.Id });

        builder.Property(x => x.TenantId)
            .HasMaxLength(64);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.StartsAt)
            .HasColumnType("datetimeoffset(7)");

        builder.Property(x => x.EndsAt)
            .HasColumnType("datetimeoffset(7)");

        builder.HasIndex(x => new { x.TenantId, x.StartsAt });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
