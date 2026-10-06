using SistemiPos.Domain.Common;

namespace SistemiPos.Domain.Audit;

public sealed class AuditLog : Entity
{
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string RecordType { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public string? UserName { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? ChangesJson { get; set; }
}
