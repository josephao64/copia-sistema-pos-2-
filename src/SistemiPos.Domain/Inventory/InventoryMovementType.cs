namespace SistemiPos.Domain.Inventory;

public enum InventoryMovementType
{
    PurchaseEntry = 1,
    SaleExit = 2,
    PositiveAdjustment = 3,
    NegativeAdjustment = 4,
    ReturnEntry = 5,
    CancellationReversal = 6
}
