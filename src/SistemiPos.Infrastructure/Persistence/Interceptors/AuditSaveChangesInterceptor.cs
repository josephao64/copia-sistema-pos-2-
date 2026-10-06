using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SistemiPos.Application.Abstractions;
using SistemiPos.Domain.Audit;
using SistemiPos.Domain.Common;

namespace SistemiPos.Infrastructure.Persistence.Interceptors;

internal sealed class AuditSaveChangesInterceptor(
    ICurrentUserAccessor currentUserAccessor,
    IDateTimeProvider dateTimeProvider) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var now = dateTimeProvider.UtcNow;
        var userName = currentUserAccessor.UserName;
        var auditEntries = new List<AuditLog>();

        foreach (var entry in eventData.Context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.State is EntityState.Detached or EntityState.Unchanged)
            {
                continue;
            }

            if (entry.Entity is AuditableEntity auditableEntity)
            {
                if (entry.State == EntityState.Added)
                {
                    auditableEntity.CreatedAtUtc = now;
                    auditableEntity.CreatedBy = userName;
                }

                auditableEntity.ModifiedAtUtc = now;
                auditableEntity.ModifiedBy = userName;
            }

            if (entry.Entity is not Entity entity)
            {
                continue;
            }

            var changes = entry.Properties.ToDictionary(
                property => property.Metadata.Name,
                property => new
                {
                    Original = entry.State == EntityState.Added ? null : property.OriginalValue,
                    Current = entry.State == EntityState.Deleted ? null : property.CurrentValue
                });

            auditEntries.Add(new AuditLog
            {
                Module = "Base",
                Action = entry.State.ToString(),
                RecordType = entry.Entity.GetType().Name,
                RecordId = entity.Id.ToString(),
                UserName = userName,
                OccurredAtUtc = now,
                ChangesJson = JsonSerializer.Serialize(changes)
            });
        }

        if (auditEntries.Count > 0)
        {
            eventData.Context.Set<AuditLog>().AddRange(auditEntries);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
