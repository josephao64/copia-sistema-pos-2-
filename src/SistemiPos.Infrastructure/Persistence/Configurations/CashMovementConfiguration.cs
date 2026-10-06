using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemiPos.Domain.Cash;

namespace SistemiPos.Infrastructure.Persistence.Configurations;

internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("CashMovements");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.UserName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.AuthorizedByUserName).HasMaxLength(256);
        builder.Property(x => x.SourceType).HasMaxLength(100);
        builder.Property(x => x.SourceDocument).HasMaxLength(100);
        builder.Property(x => x.CreatedBy).HasMaxLength(256);
        builder.Property(x => x.ModifiedBy).HasMaxLength(256);
        builder.HasIndex(x => new { x.SourceType, x.SourceId });
    }
}
