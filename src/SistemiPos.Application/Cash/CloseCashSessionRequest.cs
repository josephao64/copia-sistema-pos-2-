namespace SistemiPos.Application.Cash;

public sealed record CloseCashSessionRequest(
    decimal CountedCash,
    string? Observation = null);
