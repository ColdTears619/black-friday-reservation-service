using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sale.Domain.Entities;

namespace Sale.Infrastructure.Persistence.Configurations;

public sealed class SaleEventItemConfiguration
    : IEntityTypeConfiguration<SaleEventItem>
{
    public void Configure(EntityTypeBuilder<SaleEventItem> builder)
    {
        builder.ToTable("SaleEventItems", table =>
        {
            table.HasCheckConstraint(
                "CK_SaleEventItems_DiscountedPrice",
                "[DiscountedPrice] > 0");

            table.HasCheckConstraint(
                "CK_SaleEventItems_InitialQuantity",
                "[InitialQuantity] > 0");

            table.HasCheckConstraint(
                "CK_SaleEventItems_RemainingQuantity",
                "[RemainingQuantity] >= 0 AND " +
                "[RemainingQuantity] <= [InitialQuantity]");
        });

        builder.HasKey(x => new
        {
            x.TenantId,
            x.SaleEventId,
            x.Sku
        });

        builder.Property(x => x.TenantId)
            .HasMaxLength(64);

        builder.Property(x => x.Sku)
            .HasMaxLength(64);

        builder.Property(x => x.DiscountedPrice)
            .HasPrecision(18, 2);

        builder.HasOne<SaleEvent>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.SaleEventId
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.Sku
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
