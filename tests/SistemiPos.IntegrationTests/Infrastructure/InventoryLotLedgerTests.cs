using Microsoft.EntityFrameworkCore;
using Npgsql;
using SistemiPos.Domain.Catalog;
using SistemiPos.Domain.Common.Exceptions;
using SistemiPos.Domain.Inventory;
using SistemiPos.Infrastructure.Persistence;

namespace SistemiPos.IntegrationTests.Infrastructure;

public sealed class InventoryLotLedgerTests
{
    [PostgresFact]
    public async Task Ledger_Persists_Fifo_Trace_Returns_And_Rejects_Concurrent_Stock_Updates()
    {
        var source = Environment.GetEnvironmentVariable("SISTEMI_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(source), "Configure SISTEMI_TEST_CONNECTION para esta prueba PostgreSQL aislada.");
        var adminBuilder = new NpgsqlConnectionStringBuilder(source) { Database = "postgres", Pooling = false };
        var databaseName = "sistemi_lot_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await create.ExecuteNonQueryAsync();
        var testBuilder = new NpgsqlConnectionStringBuilder(source) { Database = databaseName, Pooling = false };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(testBuilder.ConnectionString).Options;
        try
        {
            Guid productId;
            await using (var db = new AppDbContext(options))
            {
                await db.Database.MigrateAsync();
                var category = new Category("Lotes", null);
                var product = new Product("LOT-1", null, "Producto", null, category.Id, "Marca", "g", 0, 0, 1, 0, false);
                productId = product.Id;
                var stock = new InventoryStock(productId);
                db.Categories.Add(category); db.Products.Add(product); db.InventoryStocks.Add(stock);
                await db.SaveChangesAsync();
                stock.ReplaceForSystem(10);
                db.InventoryMovements.Add(new InventoryMovement(productId, InventoryMovementType.PurchaseEntry,
                    10, 0, 10, DateTimeOffset.UtcNow.AddDays(-2), "test", "C1", null));
                await db.SaveChangesAsync();
                stock.ReplaceForSystem(30);
                db.InventoryMovements.Add(new InventoryMovement(productId, InventoryMovementType.PurchaseEntry,
                    20, 10, 30, DateTimeOffset.UtcNow.AddDays(-1), "test", "C2", null));
                await db.SaveChangesAsync();
            }
            await using (var first = new AppDbContext(options))
            await using (var stale = new AppDbContext(options))
            {
                var stock1 = await first.InventoryStocks.SingleAsync();
                var stock2 = await stale.InventoryStocks.SingleAsync();
                stock1.ReplaceForSystem(15); stock2.ReplaceForSystem(29);
                first.InventoryMovements.Add(new InventoryMovement(productId, InventoryMovementType.SaleExit,
                    15, 30, 15, DateTimeOffset.UtcNow, "test", "V1", null));
                stale.InventoryMovements.Add(new InventoryMovement(productId, InventoryMovementType.SaleExit,
                    1, 30, 29, DateTimeOffset.UtcNow, "test", "V2", null));
                await first.SaveChangesAsync();
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
            }
            await using (var db = new AppDbContext(options))
            {
                Assert.Equal(2, await db.InventoryExpiryLots.CountAsync());
                var quantities = await db.InventoryMovementLots.Where(x => x.Quantity < 0).Select(x => x.Quantity).ToListAsync();
                Assert.Equal(new[] { -10m, -5m }, quantities.OrderBy(x => x));
                var stock = await db.InventoryStocks.SingleAsync();
                Assert.Equal(15, stock.CurrentStock);
                stock.ReplaceForSystem(30);
                db.InventoryMovements.Add(new InventoryMovement(productId, InventoryMovementType.ReturnEntry,
                    15, 15, 30, DateTimeOffset.UtcNow, "test", "V1", null));
                await db.SaveChangesAsync();
                Assert.Equal(30, await db.InventoryExpiryLots.SumAsync(x => x.AvailableQuantity));
                Assert.Equal(2, await db.InventoryMovementLots.CountAsync(x => x.ReversesAllocationId != null));
            }
            await using (var db = new AppDbContext(options))
            {
                var product = await db.Products.SingleAsync();
                product.ConfigureExpiry(true, 10);
                var stock = await db.InventoryStocks.SingleAsync();
                stock.ReplaceForSystem(31);
                db.InventoryMovements.Add(new InventoryMovement(productId, InventoryMovementType.PurchaseEntry,
                    1, 30, 31, DateTimeOffset.UtcNow, "test", "BAD", null));
                await Assert.ThrowsAsync<DomainRuleException>(() => db.SaveChangesAsync());
            }
            await using (var db = new AppDbContext(options))
                Assert.Equal(30, (await db.InventoryStocks.SingleAsync()).CurrentStock);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SISTEMI_TEST_CONNECTION")))
            Skip = "Requiere SISTEMI_TEST_CONNECTION; crea y elimina una base PostgreSQL temporal.";
    }
}
