using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sale.Domain.Entities;

namespace Sale.Infrastructure.Persistence.Configurations;

public sealed class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.ToTable("ProcessingJobs");
        builder.HasKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.TenantId).HasMaxLength(64);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasMaxLength(24);
        builder.HasIndex(x => new { x.TenantId, x.ReservationId }).IsUnique();
        builder.HasOne<Reservation>().WithMany()
            .HasForeignKey(x => new { x.TenantId, Id = x.ReservationId }).OnDelete(DeleteBehavior.Restrict);
    }
}
