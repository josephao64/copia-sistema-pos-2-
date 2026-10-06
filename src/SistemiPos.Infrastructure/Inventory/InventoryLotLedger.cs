using Microsoft.EntityFrameworkCore;
using SistemiPos.Domain.Catalog;
using SistemiPos.Domain.Common.Exceptions;
using SistemiPos.Domain.Inventory;
using SistemiPos.Infrastructure.Persistence;

namespace SistemiPos.Infrastructure.Inventory;

internal static class InventoryLotLedger
{
    public static async Task PrepareAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var movements = db.ChangeTracker.Entries<InventoryMovement>()
            .Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToList();
        foreach (var movement in movements)
        {
            if (db.InventoryMovementLots.Local.Any(x => x.MovementId == movement.Id)) continue;
            var product = db.Products.Local.FirstOrDefault(x => x.Id == movement.ProductId)
                ?? await db.Products.SingleAsync(x => x.Id == movement.ProductId, cancellationToken);
            if (!movement.WarehouseId.HasValue)
            {
                var changedStocks = db.ChangeTracker.Entries<InventoryStock>()
                    .Where(x => x.Entity.ProductId == movement.ProductId && x.State is EntityState.Added or EntityState.Modified)
                    .Select(x => x.Entity).ToList();
                if (changedStocks.Count != 1)
                    throw new DomainRuleException("El movimiento debe identificar una única bodega de inventario.");
                if (changedStocks[0].WarehouseId is Guid warehouseId) movement.AssignWarehouse(warehouseId);
            }
            var persisted = await db.InventoryExpiryLots.Where(x => x.ProductId == movement.ProductId
                && x.WarehouseId == movement.WarehouseId).ToListAsync(cancellationToken);
            var lots = persisted.Concat(db.InventoryExpiryLots.Local.Where(x => x.ProductId == movement.ProductId
                && x.WarehouseId == movement.WarehouseId)).DistinctBy(x => x.Id).ToList();
            var missing = movement.PreviousStock - lots.Sum(x => x.AvailableQuantity);
            if (missing > 0)
            {
                if (product.ManagesExpiry)
                    throw new DomainRuleException($"{product.DisplayName}: registra los lotes de saldo inicial con su vencimiento antes de operar.");
                var opening = new InventoryExpiryLot(product.Id, null, missing, "SALDO INICIAL",
                    DateTimeOffset.UtcNow, movement.WarehouseId, isOpeningBalance: true);
                db.InventoryExpiryLots.Add(opening);
                lots.Add(opening);
            }
            else if (missing < 0)
                throw new DomainRuleException($"El saldo de lotes de {product.DisplayName} no coincide con la existencia. Requiere conciliación.");

            if (movement.Type is InventoryMovementType.ReturnEntry or InventoryMovementType.CancellationReversal)
            {
                await RestoreAsync(db, movement, cancellationToken);
            }
            else if (movement.NewStock > movement.PreviousStock)
            {
                if (product.ManagesExpiry && !movement.EntryExpiryDate.HasValue)
                    throw new DomainRuleException($"Indica el vencimiento del lote de {product.DisplayName}.");
                var lot = new InventoryExpiryLot(product.Id,
                    product.ManagesExpiry ? movement.EntryExpiryDate : null, movement.Quantity,
                    movement.SourceDocument, movement.OccurredAtUtc, movement.WarehouseId,
                    lotNumber: movement.EntryLotNumber);
                db.InventoryExpiryLots.Add(lot);
                db.InventoryMovementLots.Add(new InventoryMovementLot(movement, lot, movement.Quantity));
            }
            else
            {
                var today = DateOnly.FromDateTime(movement.OccurredAtUtc.ToOffset(TimeSpan.FromHours(-6)).Date);
                var allocations = InventoryLotAllocation.Plan(lots, movement.Quantity, product.ManagesExpiry,
                    today, movement.SelectedLotId,
                    allowExpired: movement.Type == InventoryMovementType.NegativeAdjustment && movement.SelectedLotId.HasValue);
                foreach (var allocation in allocations)
                {
                    allocation.Lot.Consume(allocation.Quantity);
                    db.InventoryMovementLots.Add(new InventoryMovementLot(movement, allocation.Lot, -allocation.Quantity));
                }
            }
        }
    }

    private static async Task RestoreAsync(AppDbContext db, InventoryMovement movement, CancellationToken cancellationToken)
    {
        var original = await db.InventoryMovementLots.Include(x => x.Lot).Include(x => x.Movement)
            .Where(x => x.Movement.ProductId == movement.ProductId
                && x.Movement.WarehouseId == movement.WarehouseId
                && x.Movement.SourceDocument == movement.SourceDocument
                && x.Movement.Type == InventoryMovementType.SaleExit && x.Quantity < 0)
            .OrderBy(x => x.Lot.ExpiryDate).ThenBy(x => x.Lot.ReceivedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        original = original.Concat(db.InventoryMovementLots.Local.Where(x => x.Movement.ProductId == movement.ProductId
            && x.Movement.WarehouseId == movement.WarehouseId && x.Movement.SourceDocument == movement.SourceDocument
            && x.Movement.Type == InventoryMovementType.SaleExit && x.Quantity < 0)).DistinctBy(x => x.Id).ToList();
        var ids = original.Select(x => x.Id).ToList();
        var restored = await db.InventoryMovementLots.Where(x => x.ReversesAllocationId.HasValue
            && ids.Contains(x.ReversesAllocationId.Value)).ToListAsync(cancellationToken);
        restored = restored.Concat(db.InventoryMovementLots.Local.Where(x => x.ReversesAllocationId.HasValue
            && ids.Contains(x.ReversesAllocationId.Value))).DistinctBy(x => x.Id).ToList();
        var available = original.Select(x => new
        {
            Allocation = x,
            Quantity = -x.Quantity - restored.Where(y => y.ReversesAllocationId == x.Id).Sum(y => y.Quantity)
        }).Where(x => x.Quantity > 0 && (!movement.SelectedLotId.HasValue || x.Allocation.LotId == movement.SelectedLotId)).ToList();
        if (available.Sum(x => x.Quantity) < movement.Quantity)
            throw new DomainRuleException("No se puede devolver el inventario: faltan lotes originales disponibles de la venta. Requiere conciliación.");
        if (movement.Type == InventoryMovementType.ReturnEntry && !movement.SelectedLotId.HasValue
            && available.Select(x => x.Allocation.LotId).Distinct().Count() > 1
            && movement.Quantity < available.Sum(x => x.Quantity))
            throw new DomainRuleException("Selecciona el lote original para esta devolución parcial.");
        var remaining = movement.Quantity;
        foreach (var item in available)
        {
            var quantity = Math.Min(remaining, item.Quantity);
            item.Allocation.Lot.Restore(quantity);
            db.InventoryMovementLots.Add(new InventoryMovementLot(movement, item.Allocation.Lot, quantity, item.Allocation.Id));
            remaining -= quantity;
            if (remaining == 0) break;
        }
    }
}
