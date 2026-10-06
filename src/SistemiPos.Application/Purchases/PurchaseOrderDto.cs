using SistemiPos.Domain.Purchases;

namespace SistemiPos.Application.Purchases;

public sealed record PurchaseOrderDto(
    Guid Id,
    string Number,
    Guid SupplierId,
    string SupplierName,
    DateTimeOffset RequestedAtUtc,
    string UserName,
    PurchaseOrderStatus Status,
    int ElapsedDays,
    IReadOnlyList<PurchaseOrderItemDto> Items,
    DateTimeOffset? SubmittedAtUtc = null,
    DateTimeOffset? ReceivedAtUtc = null,
    string? Observations = null,
    string? ReceiptNumber = null,
    string? SupplierDocumentNumber = null,
    DateTimeOffset? SupplierDocumentDateUtc = null,
    PurchaseTermsType? ReceiptTerms = null,
    int? CreditDays = null,
    DateTimeOffset? DueDateUtc = null,
    string? ReceiptObservations = null,
    decimal ActualTotal = 0,
    Guid? PurchaseId = null,
    string? SubmittedBy = null,
    string? ReceivedBy = null);

public sealed record PurchaseOrderItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal RequestedQuantity,
    decimal ReceivedQuantity,
    decimal PendingQuantity,
    string? SupplierInvoiceName = null,
    string? SystemDescription = null,
    string? SupplierDescription = null,
    decimal ActualUnitCost = 0,
    bool IsAdditional = false,
    decimal ActualSubtotal = 0,
    Guid? PresentationId = null,
    string PresentationName = "Unidad",
    decimal PackageQuantity = 0,
    int UnitsPerPackage = 1);
