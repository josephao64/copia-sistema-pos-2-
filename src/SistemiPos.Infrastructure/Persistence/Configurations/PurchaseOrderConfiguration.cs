using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemiPos.Domain.Purchases;

namespace SistemiPos.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(50).IsRequired();
        builder.Property(x => x.UserName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.SubmittedBy).HasMaxLength(256);
        builder.Property(x => x.ReceivedBy).HasMaxLength(256);
        builder.Property(x => x.Observations).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasMaxLength(256);
        builder.Property(x => x.ModifiedBy).HasMaxLength(256);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Purchase)
            .WithOne(x => x.PurchaseOrder)
            .HasForeignKey<Purchase>(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
