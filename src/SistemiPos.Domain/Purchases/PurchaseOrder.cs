using SistemiPos.Domain.Common;
using SistemiPos.Domain.Common.Exceptions;
using SistemiPos.Domain.Suppliers;

namespace SistemiPos.Domain.Purchases;

public sealed class PurchaseOrder : AuditableEntity
{
    private readonly List<PurchaseOrderItem> _items = [];
    public string Number { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Supplier Supplier { get; set; } = null!;
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public string? SubmittedBy { get; private set; }
    public DateTimeOffset? ReceivedAtUtc { get; private set; }
    public string? ReceivedBy { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public string? Observations { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public Purchase? Purchase { get; set; }
    public PurchaseReceipt? Receipt { get; set; }
    public IReadOnlyCollection<PurchaseOrderItem> Items => _items;
    private PurchaseOrder() { }

    public PurchaseOrder(string number, Guid supplierId, DateTimeOffset requestedAtUtc, string userName,
        IEnumerable<PurchaseOrderItem> items, string? observations = null)
    {
        if (string.IsNullOrWhiteSpace(number) || supplierId == Guid.Empty)
            throw new DomainRuleException("El número y el proveedor de la orden son requeridos.");
        var values = ValidateItems(items);
        Number = number.Trim(); SupplierId = supplierId; RequestedAtUtc = requestedAtUtc;
        UserName = string.IsNullOrWhiteSpace(userName) ? "system" : userName.Trim();
        Observations = Normalize(observations); _items.AddRange(values);
    }

    public void Update(Guid supplierId, IEnumerable<PurchaseOrderItem> items, string? observations)
    {
        EnsureEditable();
        if (supplierId == Guid.Empty) throw new DomainRuleException("El proveedor es requerido.");
        var values = ValidateItems(items);
        foreach (var existingItem in _items.ToList())
        {
            var updatedItem = values.FirstOrDefault(x => x.ProductId == existingItem.ProductId);
            if (updatedItem is null)
            {
                _items.Remove(existingItem);
                continue;
            }

            existingItem.UpdatePresentation(updatedItem.PackageQuantity, updatedItem.PresentationId,
                updatedItem.PresentationName, updatedItem.UnitsPerPackage);
        }

        var existingProductIds = _items.Select(x => x.ProductId).ToHashSet();
        _items.AddRange(values.Where(x => !existingProductIds.Contains(x.ProductId)));
        SupplierId = supplierId;
        Observations = Normalize(observations);
    }

    public void Submit(DateTimeOffset submittedAtUtc, string userName)
    {
        EnsureEditable(); Status = PurchaseOrderStatus.Requested;
        if (!SubmittedAtUtc.HasValue)
        {
            SubmittedAtUtc = submittedAtUtc;
            SubmittedBy = string.IsNullOrWhiteSpace(userName) ? "system" : userName.Trim();
        }
    }

    public void CompleteReceipt(DateTimeOffset receivedAtUtc, string userName)
    {
        if (Status != PurchaseOrderStatus.Requested)
            throw new DomainRuleException("Solo una orden solicitada puede recibirse.");
        ReceivedAtUtc = receivedAtUtc;
        ReceivedBy = string.IsNullOrWhiteSpace(userName) ? "system" : userName.Trim();
        Status = PurchaseOrderStatus.Received;
    }

    public void Cancel()
    {
        if (Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
            throw new DomainRuleException("La orden no puede cancelarse en su estado actual.");
        Status = PurchaseOrderStatus.Cancelled;
    }

    private void EnsureEditable()
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Requested))
            throw new DomainRuleException("La orden no puede editarse en su estado actual.");
    }

    private static List<PurchaseOrderItem> ValidateItems(IEnumerable<PurchaseOrderItem> items)
    {
        var values = items.ToList();
        if (values.Count == 0) throw new DomainRuleException("La orden debe incluir al menos un producto.");
        if (values.GroupBy(x => x.ProductId).Any(x => x.Count() > 1))
            throw new DomainRuleException("El producto ya forma parte de la orden.");
        return values;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
