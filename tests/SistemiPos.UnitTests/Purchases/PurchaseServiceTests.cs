using SistemiPos.Application.Abstractions;
using SistemiPos.Application.Purchases;
using SistemiPos.Domain.Catalog;
using SistemiPos.Domain.Common;
using SistemiPos.Domain.Common.Exceptions;
using SistemiPos.Domain.Inventory;
using SistemiPos.Domain.Purchases;
using SistemiPos.Domain.Suppliers;

namespace SistemiPos.UnitTests.Purchases;

public sealed class PurchaseServiceTests
{
    [Fact]
    public async Task Purchase_Complete_Flow_Creates_Confirms_And_Updates_Stock()
    {
        var product = new Product("P-COMP-1", null, "Café", null, Guid.NewGuid(), "Marca", "Unidad", 10m, 8m, 15m, 2m, true);
        var supplier = new Supplier("Proveedor Uno", "1234567", null, null, null, null, null);
        var placeholder = new Purchase("C-OLD", supplier.Id, DateTimeOffset.UtcNow, "tester", [new PurchaseItem(product.Id, 1m, 10m)]);
        var repository = new FakePurchaseRepository(product, supplier, placeholder);
        var service = new PurchaseService(repository, new FakeUnitOfWork(), new FakeCurrentUserAccessor(), new FakeDateTimeProvider());

        var purchaseId = await service.CreatePurchaseAsync(new SavePurchaseRequest(supplier.Id, DateTimeOffset.UtcNow, [new PurchaseItemRequest(product.Id, 3m, 12m)]));
        Assert.NotNull(repository.CreatedPurchase);
        var created = repository.CreatedPurchase!;
        foreach (var item in created.Items) { item.Product = product; }
        created.Supplier = supplier;
        await service.ConfirmPurchaseAsync(purchaseId);

        Assert.Equal(DocumentStatus.Confirmed, created.Status);
        Assert.Equal(3m, repository.Stock!.CurrentStock);
        Assert.Equal(12m, product.LastCost);
        Assert.Equal(InventoryMovementType.PurchaseEntry, Assert.Single(repository.Movements).Type);
    }

    [Fact]
    public async Task ConfirmPurchaseAsync_Updates_Stock_Costs_And_Movement()
    {
        var categoryId = Guid.NewGuid();
        var product = new Product("P-001", null, "Café", null, categoryId, "Marca", "Unidad", 10m, 8m, 15m, 2m, true);
        var supplier = new Supplier("Proveedor Uno", "1234567", null, null, null, null, null);
        var purchase = new Purchase(
            "C-000001",
            supplier.Id,
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero),
            "tester",
            [new PurchaseItem(product.Id, 3m, 12m)]);

        purchase.Supplier = supplier;
        foreach (var item in purchase.Items)
        {
            item.Product = product;
        }

        var repository = new FakePurchaseRepository(product, supplier, purchase);
        var unitOfWork = new FakeUnitOfWork();
        var service = new PurchaseService(
            repository,
            unitOfWork,
            new FakeCurrentUserAccessor(),
            new FakeDateTimeProvider());

        await service.ConfirmPurchaseAsync(purchase.Id);

        Assert.Equal(DocumentStatus.Confirmed, purchase.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
        Assert.NotNull(repository.Stock);
        Assert.Equal(3m, repository.Stock!.CurrentStock);
        Assert.Equal(12m, product.LastCost);
        Assert.Equal(10m, product.PreviousCost);
        Assert.Single(repository.Movements);
        Assert.Equal(InventoryMovementType.PurchaseEntry, repository.Movements[0].Type);
        Assert.Equal("C-000001", repository.Movements[0].SourceDocument);
        Assert.Equal(0m, repository.Movements[0].PreviousStock);
        Assert.Equal(3m, repository.Movements[0].NewStock);
    }

    [Fact]
    public async Task ConfirmPurchaseAsync_Rejects_Already_Confirmed_Purchase()
    {
        var categoryId = Guid.NewGuid();
        var product = new Product("P-001", null, "Café", null, categoryId, "Marca", "Unidad", 10m, 8m, 15m, 2m, true);
        var supplier = new Supplier("Proveedor Uno", "1234567", null, null, null, null, null);
        var purchase = new Purchase(
            "C-000001",
            supplier.Id,
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero),
            "tester",
            [new PurchaseItem(product.Id, 3m, 12m)]);

        purchase.Supplier = supplier;
        foreach (var item in purchase.Items)
        {
            item.Product = product;
        }

        purchase.Confirm();

        var repository = new FakePurchaseRepository(product, supplier, purchase);
        var service = new PurchaseService(
            repository,
            new FakeUnitOfWork(),
            new FakeCurrentUserAccessor(),
            new FakeDateTimeProvider());

        await Assert.ThrowsAsync<DomainRuleException>(() => service.ConfirmPurchaseAsync(purchase.Id));
    }

    [Fact]
    public async Task CreateDirectPurchaseAsync_Rejects_An_Invoice_Already_Registered_By_A_Receipt_For_The_Same_Supplier()
    {
        var product = new Product("P-INVOICE", null, "Café", null, Guid.NewGuid(), "Marca", "Unidad", 10m, 8m, 15m, 2m, true);
        var supplier = new Supplier("Proveedor Uno", "1234567", null, null, null, null, null);
        var existingPurchase = new Purchase("C-EXISTING", supplier.Id, DateTimeOffset.UtcNow, "tester",
            [new PurchaseItem(product.Id, 1m, 10m)], purchaseOrderId: Guid.NewGuid(), invoiceNumber: "FAC-45892");
        var repository = new FakePurchaseRepository(product, supplier, existingPurchase);
        var service = new PurchaseService(repository, new FakeUnitOfWork(), new FakeCurrentUserAccessor(), new FakeDateTimeProvider());

        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => service.CreateDirectPurchaseAsync(
            new CreateDirectPurchaseRequest(supplier.Id, DateTimeOffset.UtcNow, "FAC-45892", "1234567",
                PurchaseTermsType.Cash, null, null,
                [new DirectPurchaseItemRequest(product.Id, 1m, 1, 10m, SupplierPackageType.Unit, "Café", null)])));

        Assert.Equal("La factura FAC-45892 ya fue registrada para este proveedor.", exception.Message);
        Assert.Null(repository.CreatedPurchase);
        Assert.Empty(repository.Movements);
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_Rejects_An_Invoice_Already_Registered_By_A_Direct_Purchase_For_The_Same_Supplier()
    {
        var product = new Product("P-INVOICE-REC", null, "Azúcar", null, Guid.NewGuid(), "Marca", "Unidad", 10m, 8m, 15m, 2m, true);
        var supplier = new Supplier("Proveedor Uno", "1234567", null, null, null, null, null);
        var directPurchase = new Purchase("C-DIRECT", supplier.Id, DateTimeOffset.UtcNow, "tester",
            [new PurchaseItem(product.Id, 1m, 10m)], invoiceNumber: "FAC-45892");
        var order = new PurchaseOrder("PC-INVOICE", supplier.Id, DateTimeOffset.UtcNow, "tester",
            [new PurchaseOrderItem(product.Id, 1m)]);
        order.Submit(DateTimeOffset.UtcNow, "tester");
        var repository = new FakePurchaseRepository(product, supplier, directPurchase) { Order = order };
        var service = new PurchaseService(repository, new FakeUnitOfWork(), new FakeCurrentUserAccessor(), new FakeDateTimeProvider());

        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => service.ReceivePurchaseOrderAsync(order.Id,
            new ReceivePurchaseOrderRequest([], "FAC-45892", DateTimeOffset.UtcNow, SupplierNit: "1234567")));

        Assert.Equal("La factura FAC-45892 ya fue registrada para este proveedor.", exception.Message);
        Assert.Null(repository.CreatedPurchase);
        Assert.Empty(repository.Movements);
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_Allows_Overdelivery_And_Prevents_A_Second_Receipt()
    {
        var product = new Product("P-002", null, "Lápices", null, Guid.NewGuid(), "Marca", "Unidad", 1m, 1m, 2m, 2m, true);
        var supplier = new Supplier("Proveedor", "7654321", null, null, null, null, null);
        var purchase = new Purchase("C-000001", supplier.Id, DateTimeOffset.UtcNow, "tester", [new PurchaseItem(product.Id, 1m, 1m)]);
        purchase.Supplier = supplier;
        var order = new PurchaseOrder("PC-000001", supplier.Id, DateTimeOffset.UtcNow, "tester", [new PurchaseOrderItem(product.Id, 5m)]);
        order.Submit(DateTimeOffset.UtcNow, "tester");
        order.Supplier = supplier;
        foreach (var item in order.Items) item.Product = product;

        var repository = new FakePurchaseRepository(product, supplier, purchase) { Order = order };
        var service = new PurchaseService(repository, new FakeUnitOfWork(), new FakeCurrentUserAccessor(), new FakeDateTimeProvider());
        var itemId = order.Items.Single().Id;

        await service.ReceivePurchaseOrderAsync(order.Id, new ReceivePurchaseOrderRequest(
            [new ReceivePurchaseOrderItemRequest(itemId, 7m, 1.25m)], "FAC-100", DateTimeOffset.UtcNow,
            SupplierNit: "7654321"));

        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Equal(7m, repository.Stock!.CurrentStock);
        Assert.Equal(7m, Assert.Single(repository.Movements).Quantity);
        Assert.Equal(order.Id, repository.CreatedPurchase!.PurchaseOrderId);
        Assert.NotNull(repository.CreatedReceipt);
        Assert.Equal(repository.CreatedPurchase.Id, repository.CreatedReceipt!.PurchaseId);
        await Assert.ThrowsAsync<DomainRuleException>(() => service.ReceivePurchaseOrderAsync(order.Id,
            new ReceivePurchaseOrderRequest([new ReceivePurchaseOrderItemRequest(itemId, 1m, 1m)], "FAC-101", DateTimeOffset.UtcNow,
                SupplierNit: "7654321")));
    }

    [Fact]
    public async Task PurchaseOrder_Complete_Flow_Creates_Receives_And_Updates_Kardex()
    {
        var product = new Product("P-ORD-1", null, "Cuadernos", null, Guid.NewGuid(), "Marca", "Unidad", 5m, 5m, 9m, 2m, true);
        var supplier = new Supplier("Proveedor pedidos", "8765432", null, null, null, null, null);
        var placeholder = new Purchase("C-PLACEHOLDER", supplier.Id, DateTimeOffset.UtcNow, "tester", [new PurchaseItem(product.Id, 1m, 5m)]);
        var repository = new FakePurchaseRepository(product, supplier, placeholder);
        var service = new PurchaseService(repository, new FakeUnitOfWork(), new FakeCurrentUserAccessor(), new FakeDateTimeProvider());

        var orderId = await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(supplier.Id, [new PurchaseOrderItemRequest(product.Id, 6m)], Submit: true));
        Assert.NotNull(repository.Order);
        var order = repository.Order!;
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 13, 0, 0, TimeSpan.Zero), order.RequestedAtUtc);
        var item = Assert.Single(order.Items);
        await service.ReceivePurchaseOrderAsync(orderId, new ReceivePurchaseOrderRequest(
            [new ReceivePurchaseOrderItemRequest(item.Id, 6m, 5.5m)], "FAC-200", DateTimeOffset.UtcNow,
            SupplierNit: "8765432"));

        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Equal(6m, repository.Stock!.CurrentStock);
        Assert.Equal(InventoryMovementType.PurchaseEntry, Assert.Single(repository.Movements).Type);
        Assert.Equal(DocumentStatus.Confirmed, repository.CreatedPurchase!.Status);
    }

    [Fact]
    public async Task ReceivePurchaseOrderAsync_Creates_Payable_Only_For_Credit()
    {
        var product = new Product("P-CREDIT", null, "Papel", null, Guid.NewGuid(), "Marca", "Unidad", 4m, 3m, 7m, 1m, true);
        var supplier = new Supplier("Proveedor crédito", "1122334", null, null, null, null, null);
        var placeholder = new Purchase("C-OLD", supplier.Id, DateTimeOffset.UtcNow, "tester", [new PurchaseItem(product.Id, 1m, 1m)]);
        var order = new PurchaseOrder("PC-000099", supplier.Id, DateTimeOffset.UtcNow, "tester", [new PurchaseOrderItem(product.Id, 2m)]);
        order.Supplier = supplier;
        foreach (var item in order.Items) item.Product = product;
        order.Submit(DateTimeOffset.UtcNow, "tester");
        var repository = new FakePurchaseRepository(product, supplier, placeholder) { Order = order };
        var service = new PurchaseService(repository, new FakeUnitOfWork(), new FakeCurrentUserAccessor(), new FakeDateTimeProvider());
        var documentDate = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);

        await service.ReceivePurchaseOrderAsync(order.Id, new ReceivePurchaseOrderRequest(
            [new ReceivePurchaseOrderItemRequest(order.Items.Single().Id, 2m, 4.5m)],
            "FAC-CRED-1", documentDate, PurchaseTermsType.Credit, 30, SupplierNit: "1122334"));

        Assert.NotNull(repository.Payable);
        Assert.Equal("FAC-CRED-1", repository.Payable!.InvoiceNumber);
        Assert.Equal(documentDate.AddDays(30), repository.Payable.DueDateUtc);
        Assert.Equal(9m, repository.Payable.Total);
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_Preserves_Presentation_And_Equivalent_Units()
    {
        var product = new Product("P-ORD-PRES", null, "Coca Cola 600 ml", null, Guid.NewGuid(), "Marca", "Unidad", 5m, 5m, 9m, 2m, true);
        var supplier = new Supplier("Proveedor pedidos", "8765432", null, null, null, null, null);
        var presentation = new PurchasePresentation("Caja", 24);
        var placeholder = new Purchase("C-PLACEHOLDER", supplier.Id, DateTimeOffset.UtcNow, "tester", [new PurchaseItem(product.Id, 1m, 5m)]);
        var repository = new FakePurchaseRepository(product, supplier, placeholder, presentation);
        var service = new PurchaseService(repository, new FakeUnitOfWork(), new FakeCurrentUserAccessor(), new FakeDateTimeProvider());

        await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(supplier.Id,
            [new PurchaseOrderItemRequest(product.Id, 2m, presentation.Id)]));

        var item = Assert.Single(repository.Order!.Items);
        Assert.Equal(presentation.Id, item.PresentationId);
        Assert.Equal("Caja", item.PresentationName);
        Assert.Equal(2m, item.PackageQuantity);
        Assert.Equal(24, item.UnitsPerPackage);
        Assert.Equal(48m, item.RequestedQuantity);
    }

    [Fact]
    public async Task UpdatePurchaseOrderAsync_Preserves_Existing_Line_And_Submits_Draft()
    {
        var product = new Product("P-ORD-EDIT", null, "Cuadernos", null, Guid.NewGuid(), "Marca", "Unidad", 5m, 5m, 9m, 2m, true);
        var supplier = new Supplier("Proveedor pedidos", "8765432", null, null, null, null, null);
        var placeholder = new Purchase("C-PLACEHOLDER", supplier.Id, DateTimeOffset.UtcNow, "tester", [new PurchaseItem(product.Id, 1m, 5m)]);
        var order = new PurchaseOrder("PC-000001", supplier.Id, DateTimeOffset.UtcNow, "tester",
            [new PurchaseOrderItem(product.Id, 2m)]);
        var originalItemId = order.Items.Single().Id;
        var repository = new FakePurchaseRepository(product, supplier, placeholder) { Order = order };
        var service = new PurchaseService(repository, new FakeUnitOfWork(), new FakeCurrentUserAccessor(), new FakeDateTimeProvider());

        await service.UpdatePurchaseOrderAsync(order.Id,
            new UpdatePurchaseOrderRequest(supplier.Id, [new PurchaseOrderItemRequest(product.Id, 5m)], null, true));

        var updatedItem = Assert.Single(order.Items);
        Assert.Equal(originalItemId, updatedItem.Id);
        Assert.Equal(5m, updatedItem.RequestedQuantity);
        Assert.Equal(PurchaseOrderStatus.Requested, order.Status);
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_Rejects_Duplicate_Products()
    {
        var product = new Product("P-ORD-1", null, "Cuadernos", null, Guid.NewGuid(), "Marca", "Unidad", 5m, 5m, 9m, 2m, true);
        var supplier = new Supplier("Proveedor pedidos", "8765432", null, null, null, null, null);
        var placeholder = new Purchase("C-PLACEHOLDER", supplier.Id, DateTimeOffset.UtcNow, "tester", [new PurchaseItem(product.Id, 1m, 5m)]);
        var repository = new FakePurchaseRepository(product, supplier, placeholder);
        var service = new PurchaseService(repository, new FakeUnitOfWork(), new FakeCurrentUserAccessor(), new FakeDateTimeProvider());

        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => service.CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequest(supplier.Id,
            [
                new PurchaseOrderItemRequest(product.Id, 2m),
                new PurchaseOrderItemRequest(product.Id, 3m)
            ])));

        Assert.Equal("El producto ya forma parte de la orden.", exception.Message);
        Assert.Null(repository.Order);
    }

    [Fact]
    public async Task Product_Presentation_Receives_Grams_And_Preserves_Factor_And_Unit_Cost()
    {
        var product = new Product("PEP-001", null, "Pepperoni", null, Guid.NewGuid(), "Sin marca", "g", 0, 0, 0, 500, false);
        product.SetBaseUnit(Guid.NewGuid(), "g");
        var supplier = new Supplier("Proveedor", "123", null, null, null, null, null);
        var placeholder = new Purchase("OLD", supplier.Id, DateTimeOffset.UtcNow, "tester", [new PurchaseItem(product.Id, 1, 1)]);
        var repository = new FakePurchaseRepository(product, supplier, placeholder);
        var presentation = new ProductPurchasePresentation(product.Id, "Bolsa 2.5 kg", 2500);
        repository.ProductPresentations.Add(presentation);
        var service = new PurchaseService(repository, new FakeUnitOfWork(), new FakeCurrentUserAccessor(), new FakeDateTimeProvider());
        await service.CreateDirectPurchaseAsync(new CreateDirectPurchaseRequest(supplier.Id, DateTimeOffset.UtcNow,
            "FACT-2500", "123", PurchaseTermsType.Cash, null, null,
            [new DirectPurchaseItemRequest(product.Id, 2, 999, 10, SupplierPackageType.Package,
                "Pepperoni", null, PresentationId: presentation.Id)]));
        var item = Assert.Single(repository.CreatedPurchase!.Items);
        Assert.Equal(5000, item.Quantity);
        Assert.Equal(2500, item.BaseQuantityPerPackage);
        Assert.Equal("g", item.BaseUnitCode);
        Assert.Equal(0.004m, item.UnitCost);
        Assert.Equal(0.004m, product.LastCost);
        Assert.Equal(5000, repository.Stock!.CurrentStock);
        presentation.Update("Bolsa nueva", 1000);
        Assert.Equal(2500, item.BaseQuantityPerPackage);
    }

    private sealed class FakePurchaseRepository(Product product, Supplier supplier, Purchase purchase,
        PurchasePresentation? presentation = null) : IPurchaseRepository
    {
        public List<InventoryMovement> Movements { get; } = [];
        public InventoryStock? Stock { get; private set; }
        public PurchaseOrder? Order { get; set; }
        public Purchase? CreatedPurchase { get; private set; }
        public SistemiPos.Domain.Finance.PayableAccount? Payable { get; private set; }
        public PurchaseReceipt? CreatedReceipt { get; private set; }

        public Task AddMovementAsync(InventoryMovement movement, CancellationToken cancellationToken = default)
        {
            Movements.Add(movement);
            return Task.CompletedTask;
        }

        public Task AddPurchaseAsync(Purchase value, CancellationToken cancellationToken = default) { CreatedPurchase = value; return Task.CompletedTask; }

        public Task<Purchase?> GetPurchaseByOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Purchase?>(CreatedPurchase?.PurchaseOrderId == purchaseOrderId ? CreatedPurchase : null);

        public Task<bool> InvoiceExistsForSupplierAsync(Guid supplierId, string invoiceNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(purchase.SupplierId == supplierId
                && string.Equals(purchase.InvoiceNumber, invoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase));

        public Task AddPayableAccountAsync(SistemiPos.Domain.Finance.PayableAccount account, CancellationToken cancellationToken = default)
        {
            Payable = account;
            return Task.CompletedTask;
        }

        public Task AddPurchaseReceiptAsync(PurchaseReceipt receipt, CancellationToken cancellationToken = default)
        {
            CreatedReceipt = receipt;
            return Task.CompletedTask;
        }

        public Task AddStockAsync(InventoryStock stock, CancellationToken cancellationToken = default)
        {
            Stock = stock;
            return Task.CompletedTask;
        }

        public Task<string> GeneratePurchaseNumberAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("C-000001");

        public Task<string> GeneratePurchaseOrderNumberAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("PC-000001");

        public Task AddPurchaseOrderAsync(PurchaseOrder order, CancellationToken cancellationToken = default)
        {
            Order = order;
            return Task.CompletedTask;
        }

        public Task<PurchaseOrder?> GetPurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (Order?.Id != id)
            {
                return Task.FromResult<PurchaseOrder?>(null);
            }

            Order.Supplier = supplier;
            foreach (var item in Order.Items.Where(x => x.ProductId == product.Id))
            {
                item.Product = product;
            }

            return Task.FromResult<PurchaseOrder?>(Order);
        }

        public Task<List<PurchaseOrder>> GetPurchaseOrdersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Order is null ? new List<PurchaseOrder>() : new List<PurchaseOrder> { Order });

        public List<ProductPurchasePresentation> ProductPresentations { get; } = [];
        public Task<ProductPurchasePresentation?> GetProductPurchasePresentationAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProductPresentations.FirstOrDefault(x => x.Id == id));
        public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Product?>(id == product.Id ? product : null);

        public Task<PurchasePresentation?> GetPurchasePresentationAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PurchasePresentation?>(presentation?.Id == id ? presentation : null);

        public Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Purchase?>(CreatedPurchase?.Id == id ? CreatedPurchase : id == purchase.Id ? purchase : null);

        public Task<List<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Purchase> { purchase });

        public Task<List<Purchase>> GetPurchasesBySupplierAsync(Guid supplierId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Purchase> { purchase });

        public Task<InventoryStock?> GetStockAsync(Guid productId, CancellationToken cancellationToken = default) =>
            Task.FromResult<InventoryStock?>(Stock);

        public Task<Supplier?> GetSupplierAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Supplier?>(id == supplier.Id ? supplier : null);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeCurrentUserAccessor : ICurrentUserAccessor
    {
        public Guid? UserId => Guid.NewGuid();
        public string UserName => "tester";
        public bool IsAuthenticated => true;
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 8, 30, 13, 0, 0, TimeSpan.Zero);
    }
}
