namespace SistemiPos.Application.Inventory;

public sealed class InventoryQueryService(IInventoryRepository repository, SistemiPos.Application.Abstractions.IDateTimeProvider dateTimeProvider) : IInventoryQueryService
{
    public Task<IReadOnlyList<InventoryStockDto>> GetCurrentStockAsync(
        bool onlyLowStock,
        CancellationToken cancellationToken = default) =>
        GetCurrentStockAsync(onlyLowStock, null, null, cancellationToken);

    public async Task<IReadOnlyList<InventoryStockDto>> GetCurrentStockAsync(
        bool onlyLowStock,
        Guid? branchId,
        Guid? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var stocks = await repository.GetStocksAsync(onlyLowStock, branchId, warehouseId, cancellationToken);
        return stocks.OrderBy(x => x.ProductName).ToList();
    }

    public Task<InventoryMovementPage> GetKardexAsync(
        Guid? productId,
        int page = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        const int defaultPageSize = 100;
        const int maximumPageSize = 500;

        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = pageSize <= 0 ? defaultPageSize : Math.Min(pageSize, maximumPageSize);

        return repository.GetMovementPageAsync(productId, normalizedPage, normalizedPageSize, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryMovementDto>> GetKardexExportAsync(
        Guid? productId,
        CancellationToken cancellationToken = default) =>
        await repository.GetMovementsForExportAsync(productId, cancellationToken);

    public async Task<IReadOnlyList<InventoryExpiryLotDto>> GetExpiryLotsAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow.ToOffset(TimeSpan.FromHours(-6)).Date);
        var lots = await repository.GetExpiryLotsAsync(null, cancellationToken);
        return lots
            .Where(x => x.Product.ManagesExpiry && x.ExpiryDate.HasValue && x.AvailableQuantity > 0)
            .Select(x =>
            {
                var days = x.ExpiryDate!.Value.DayNumber - today.DayNumber;
                var status = days < 0 ? "Caducado" : days == 0 ? "Caduca hoy" : "Próximo a caducar";
                return new { Lot = x, Value = new InventoryExpiryLotDto(x.ProductId, x.Product.DisplayName, x.Product.Category.Name,
                    x.AvailableQuantity, x.ExpiryDate!.Value, days, status) };
            })
            .Where(x => x.Value.DaysRemaining <= x.Lot.Product.ExpiryAlertDays)
            .OrderBy(x => x.Value.ExpiryDate).Select(x => x.Value).ToList();
    }

}
