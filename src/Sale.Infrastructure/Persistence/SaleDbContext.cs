using Microsoft.EntityFrameworkCore;
using Sale.Domain.Entities;

namespace Sale.Infrastructure.Persistence;

public sealed class SaleDbContext(
    DbContextOptions<SaleDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<SaleEvent> SaleEvents => Set<SaleEvent>();

    public DbSet<SaleEventItem> SaleEventItems => Set<SaleEventItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(SaleDbContext).Assembly);
    }
}
