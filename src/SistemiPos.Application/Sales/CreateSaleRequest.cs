namespace SistemiPos.Application.Sales;

public sealed record CreateSaleRequest(
    Guid CashSessionId,
    Guid? CustomerId,
    IReadOnlyList<SaleItemRequest> Items,
    SalePaymentRequest Payment,
    string? DiscountReason = null,
    string? DiscountAuthorizedByUserName = null,
    string? CustomerNit = null,
    string? CustomerName = null,
    string? CustomerAddress = null,
    DateTimeOffset? CreditDueDateUtc = null,
    string? DiscountAuthorizerPassword = null,
    bool RequirePerLineDiscountAuthorization = false,
    string? CustomerPhone = null,
    string? CustomerVisitOrigin = null,
    Guid? DiscountApprovalRequestId = null,
    string? ClientOperationId = null,
    DateTimeOffset? LocalCreatedAtUtc = null);
