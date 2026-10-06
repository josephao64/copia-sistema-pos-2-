using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemiPos.Domain.Sales;

namespace SistemiPos.Infrastructure.Persistence.Configurations;

internal sealed class SaleReturnConfiguration : IEntityTypeConfiguration<SaleReturn>
{
    public void Configure(EntityTypeBuilder<SaleReturn> builder)
    {
        builder.ToTable("SaleReturns");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Number).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.UserName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.AuthorizedByUserName).HasMaxLength(256);
        builder.Property(x => x.CreatedBy).HasMaxLength(256);
        builder.Property(x => x.ModifiedBy).HasMaxLength(256);

        builder.HasIndex(x => x.Number).IsUnique();

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.SaleReturnId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
