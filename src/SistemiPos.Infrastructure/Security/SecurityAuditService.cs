using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SistemiPos.Application.Abstractions;
using SistemiPos.Application.Security;
using SistemiPos.Domain.Audit;
using SistemiPos.Infrastructure.Persistence;

namespace SistemiPos.Infrastructure.Security;

public sealed class SecurityAuditService(AppDbContext dbContext, IDateTimeProvider dateTimeProvider) : ISecurityAuditService
{
    public async Task LogLoginSuccessAsync(string userName, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            Module = SecurityAuditActions.Module,
            Action = SecurityAuditActions.LoginSuccess,
            RecordType = "Usuario",
            RecordId = userName,
            DocumentNumber = ipAddress,
            UserName = userName,
            OccurredAtUtc = dateTimeProvider.UtcNow,
            ChangesJson = JsonSerializer.Serialize(new { Event = "Inicio de sesión correcto", IpAddress = ipAddress })
        };

        dbContext.AuditLogs.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task LogLoginFailureAsync(string identifier, string reason, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            Module = SecurityAuditActions.Module,
            Action = SecurityAuditActions.LoginFailure,
            RecordType = "Usuario",
            RecordId = identifier,
            DocumentNumber = ipAddress,
            UserName = identifier,
            OccurredAtUtc = dateTimeProvider.UtcNow,
            ChangesJson = JsonSerializer.Serialize(new { Event = "Intento de inicio de sesión fallido", Reason = reason, IpAddress = ipAddress })
        };

        dbContext.AuditLogs.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task LogAccountLockedOutAsync(string identifier, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            Module = SecurityAuditActions.Module,
            Action = SecurityAuditActions.AccountLockedOut,
            RecordType = "Usuario",
            RecordId = identifier,
            DocumentNumber = ipAddress,
            UserName = identifier,
            OccurredAtUtc = dateTimeProvider.UtcNow,
            ChangesJson = JsonSerializer.Serialize(new { Event = "Cuenta bloqueada temporalmente por intentos fallidos", IpAddress = ipAddress })
        };

        dbContext.AuditLogs.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task LogLogoutAsync(string userName, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            Module = SecurityAuditActions.Module,
            Action = SecurityAuditActions.Logout,
            RecordType = "Usuario",
            RecordId = userName,
            DocumentNumber = ipAddress,
            UserName = userName,
            OccurredAtUtc = dateTimeProvider.UtcNow,
            ChangesJson = JsonSerializer.Serialize(new { Event = "Cierre de sesión", IpAddress = ipAddress })
        };

        dbContext.AuditLogs.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task LogUserManagementAsync(string actorUserName, string action, string targetUserId, string targetUserName, string details, CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            Module = SecurityAuditActions.Module,
            Action = action,
            RecordType = "Usuario",
            RecordId = targetUserId,
            DocumentNumber = targetUserName,
            UserName = actorUserName,
            OccurredAtUtc = dateTimeProvider.UtcNow,
            ChangesJson = JsonSerializer.Serialize(new { TargetUserName = targetUserName, Details = details })
        };

        dbContext.AuditLogs.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SecurityAuditLogEntry>> GetRecentSecurityLogsAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        return await dbContext.AuditLogs
            .AsNoTracking()
            .Where(x => x.Module == SecurityAuditActions.Module)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(count)
            .Select(x => new SecurityAuditLogEntry(
                x.Id,
                x.Module,
                x.Action,
                x.RecordType,
                x.RecordId,
                x.DocumentNumber,
                x.UserName,
                x.OccurredAtUtc,
                x.ChangesJson))
            .ToListAsync(cancellationToken);
    }
}

