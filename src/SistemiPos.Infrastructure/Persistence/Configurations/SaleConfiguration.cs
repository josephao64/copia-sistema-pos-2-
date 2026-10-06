using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemiPos.Domain.Sales;

namespace SistemiPos.Infrastructure.Persistence.Configurations;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("Sales");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Number).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ClientOperationId).HasMaxLength(80);
        builder.Property(x => x.CustomerNitSnapshot).HasMaxLength(50);
        builder.Property(x => x.CustomerNameSnapshot).HasMaxLength(200);
        builder.Property(x => x.CustomerAddressSnapshot).HasMaxLength(500);
        builder.Property(x => x.CustomerPhoneSnapshot).HasMaxLength(50);
        builder.Property(x => x.CustomerVisitOriginSnapshot).HasMaxLength(150);
        builder.Property(x => x.CashierUserName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Subtotal).HasColumnType("numeric(18,2)");
        builder.Property(x => x.DiscountTotal).HasColumnType("numeric(18,2)");
        builder.Property(x => x.TaxableBaseTotal).HasColumnType("numeric(18,2)");
        builder.Property(x => x.TaxTotal).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Total).HasColumnType("numeric(18,2)");
        builder.Property(x => x.DiscountAppliedByUserName).HasMaxLength(256);
        builder.Property(x => x.DiscountReason).HasMaxLength(500);
        builder.Property(x => x.DiscountAuthorizedByUserName).HasMaxLength(256);
        builder.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.CurrencyName).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CurrencySymbol).HasMaxLength(10).IsRequired();
        builder.Property(x => x.DecimalSeparator).HasMaxLength(5).IsRequired();
        builder.Property(x => x.ThousandsSeparator).HasMaxLength(5).IsRequired();
        builder.Property(x => x.CurrencySymbolPosition).HasMaxLength(10).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(256);
        builder.Property(x => x.ModifiedBy).HasMaxLength(256);

        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.ClientOperationId)
            .IsUnique()
            .HasFilter("\"ClientOperationId\" IS NOT NULL");
        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SistemiPos.Domain.Cash.CashSession>()
            .WithMany()
            .HasForeignKey(x => x.CashSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Payment)
            .WithOne()
            .HasForeignKey<SalePayment>(x => x.SaleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
