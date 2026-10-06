using SistemiPos.Domain.Common;
using SistemiPos.Domain.Common.Exceptions;
using SistemiPos.Domain.Catalog;
using SistemiPos.Domain.Organizations;

namespace SistemiPos.Domain.Inventory;

public sealed class InventoryMovement : AuditableEntity
{
    public Guid ProductId { get; private set; }
    public Product Product { get; set; } = null!;
    public Guid? WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; set; }
    public InventoryMovementType Type { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal PreviousStock { get; private set; }
    public decimal NewStock { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public string? SourceDocument { get; private set; }
    public string? Note { get; private set; }
    public Guid? PhysicalInventoryCountId { get; private set; }
    public PhysicalInventoryCount? PhysicalInventoryCount { get; set; }
    public Guid? PhysicalInventoryCountItemId { get; private set; }
    public PhysicalInventoryCountItem? PhysicalInventoryCountItem { get; set; }
    public Guid? PurchaseOrderId { get; private set; }
    public Guid? PurchaseId { get; private set; }

    public DateOnly? EntryExpiryDate { get; private set; }
    public string? EntryLotNumber { get; private set; }
    public Guid? SelectedLotId { get; private set; }

    public void SetLotDetails(DateOnly? expiryDate, string? lotNumber = null, Guid? selectedLotId = null)
    {
        EntryExpiryDate = expiryDate;
        EntryLotNumber = lotNumber;
        SelectedLotId = selectedLotId;
    }

    private InventoryMovement()
    {
    }

    public InventoryMovement(
        Guid productId,
        InventoryMovementType type,
        decimal quantity,
        decimal previousStock,
        decimal newStock,
        DateTimeOffset occurredAtUtc,
        string userName,
        string? sourceDocument,
        string? note,
        Guid? physicalInventoryCountId = null,
        Guid? physicalInventoryCountItemId = null,
        Guid? purchaseOrderId = null,
        Guid? purchaseId = null,
        Guid? warehouseId = null)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainRuleException("El producto es requerido.");
        }

        if (quantity <= 0)
        {
            throw new DomainRuleException("La cantidad del movimiento debe ser mayor a cero.");
        }

        if (previousStock < 0 || newStock < 0)
        {
            throw new DomainRuleException("La existencia del movimiento no puede ser negativa.");
        }

        ProductId = productId;
        Type = type;
        Quantity = decimal.Round(quantity, 2);
        PreviousStock = decimal.Round(previousStock, 2);
        NewStock = decimal.Round(newStock, 2);
        OccurredAtUtc = occurredAtUtc;
        UserName = string.IsNullOrWhiteSpace(userName) ? "system" : userName.Trim();
        SourceDocument = string.IsNullOrWhiteSpace(sourceDocument) ? null : sourceDocument.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        PhysicalInventoryCountId = physicalInventoryCountId;
        PhysicalInventoryCountItemId = physicalInventoryCountItemId;
        PurchaseOrderId = purchaseOrderId;
        PurchaseId = purchaseId;
        WarehouseId = warehouseId;
    }

    public void AssignWarehouse(Guid warehouseId)
    {
        WarehouseId = warehouseId;
    }
}
