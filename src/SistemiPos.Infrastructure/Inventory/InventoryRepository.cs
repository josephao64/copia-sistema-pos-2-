using Microsoft.EntityFrameworkCore;
using SistemiPos.Application.Inventory;
using SistemiPos.Domain.Inventory;
using SistemiPos.Infrastructure.Persistence;

namespace SistemiPos.Infrastructure.Inventory;

public sealed class InventoryRepository(AppDbContext dbContext) : IInventoryRepository
{
    public Task<InventoryStock?> GetStockAsync(Guid productId, CancellationToken cancellationToken = default) =>
        dbContext.InventoryStocks.SingleOrDefaultAsync(x => x.ProductId == productId, cancellationToken);

    public Task AddMovementAsync(InventoryMovement movement, CancellationToken cancellationToken = default) =>
        dbContext.InventoryMovements.AddAsync(movement, cancellationToken).AsTask();

    public Task AddExpiryLotAsync(InventoryExpiryLot lot, CancellationToken cancellationToken = default) =>
        dbContext.InventoryExpiryLots.AddAsync(lot, cancellationToken).AsTask();

    public Task<List<InventoryExpiryLot>> GetExpiryLotsAsync(Guid? productId, CancellationToken cancellationToken = default)
    {
        var query = dbContext.InventoryExpiryLots.Include(x => x.Product).ThenInclude(x => x.Category).AsQueryable();
        if (productId.HasValue) query = query.Where(x => x.ProductId == productId.Value);
        return query.ToListAsync(cancellationToken);
    }

    public async Task<InventoryMovementPage> GetMovementPageAsync(
        Guid? productId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = BuildMovementQuery(productId);
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var currentPage = Math.Min(page, totalPages);

        var pageQuery = query
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize);

        var items = await ProjectMovements(pageQuery)
            .ToListAsync(cancellationToken);

        return new InventoryMovementPage(items, totalCount, currentPage, pageSize);
    }

    public Task<List<InventoryMovementDto>> GetMovementsForExportAsync(
        Guid? productId,
        CancellationToken cancellationToken = default) =>
        ProjectMovements(BuildMovementQuery(productId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id))
            .ToListAsync(cancellationToken);

    private IQueryable<InventoryMovement> BuildMovementQuery(Guid? productId)
    {
        var query = dbContext.InventoryMovements.AsNoTracking();

        if (productId.HasValue)
        {
            query = query.Where(x => x.ProductId == productId.Value);
        }

        return query;
    }

    private static IQueryable<InventoryMovementDto> ProjectMovements(IQueryable<InventoryMovement> query) =>
        query.Select(x => new InventoryMovementDto(
            x.Id,
            x.ProductId,
            x.Product.VariantValue == null ? x.Product.Name : x.Product.Name + " " + x.Product.VariantValue,
            x.Type,
            x.Quantity,
            x.PreviousStock,
            x.NewStock,
            x.OccurredAtUtc,
            x.UserName,
            x.SourceDocument,
            x.Note));

    public Task<List<InventoryStockDto>> GetStocksAsync(
        bool onlyLowStock,
        CancellationToken cancellationToken = default) =>
        GetStocksAsync(onlyLowStock, null, null, cancellationToken);

    public Task<List<InventoryStockDto>> GetStocksAsync(
        bool onlyLowStock,
        Guid? branchId,
        Guid? warehouseId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SistemiPos.Domain.Inventory.InventoryStock> query = dbContext.InventoryStocks
            .Include(x => x.Product)
                .ThenInclude(x => x.Category)
            .Include(x => x.Warehouse)
                .ThenInclude(w => w!.Branch)
            .AsNoTracking()
            .Where(x => !x.Product.ManagesVariants);

        if (warehouseId.HasValue)
        {
            query = query.Where(x => x.WarehouseId == warehouseId.Value);
        }
        else if (branchId.HasValue)
        {
            query = query.Where(x => x.Warehouse != null && x.Warehouse.BranchId == branchId.Value);
        }

        var projection = query.Select(x => new InventoryStockDto(
            x.ProductId,
            x.Product.InternalCode,
            x.Product.VariantValue == null ? x.Product.Name : x.Product.Name + " " + x.Product.VariantValue,
            x.Product.Category.Name,
            x.CurrentStock,
            x.Product.MinimumStock,
            x.CurrentStock > 0 && x.CurrentStock <= x.Product.MinimumStock,
            x.WarehouseId,
            x.Warehouse != null ? x.Warehouse.Name : null,
            x.Warehouse != null ? x.Warehouse.BranchId : null,
            x.Warehouse != null && x.Warehouse.Branch != null ? x.Warehouse.Branch.Name : null));

        if (onlyLowStock)
        {
            projection = projection.Where(x => x.IsLowStock);
        }

        return projection.ToListAsync(cancellationToken);
    }
}
