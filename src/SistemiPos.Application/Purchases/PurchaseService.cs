using SistemiPos.Application.Abstractions;
using SistemiPos.Application.Common.Exceptions;
using SistemiPos.Domain.Common.Exceptions;
using SistemiPos.Domain.Catalog;
using SistemiPos.Domain.Inventory;
using SistemiPos.Domain.Purchases;
using SistemiPos.Domain.Finance;

namespace SistemiPos.Application.Purchases;

public sealed class PurchaseService(
    IPurchaseRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    IDateTimeProvider dateTimeProvider) : IPurchaseService
{
    public async Task<IReadOnlyList<PurchasePresentationDto>> GetPurchasePresentationsAsync(CancellationToken cancellationToken = default) =>
        (await repository.GetPurchasePresentationsAsync(cancellationToken))
            .Select(x => new PurchasePresentationDto(x.Id, x.Name, x.Units)).ToList();

    public async Task<PurchasePresentationDto> CreatePurchasePresentationAsync(string name, int units, CancellationToken cancellationToken = default)
    {
        var normalized = SistemiPos.Domain.Common.NameNormalizer.Normalize(name);
        var presentations = await repository.GetPurchasePresentationsAsync(cancellationToken);
        if (presentations.Any(x => string.Equals(x.Name, normalized, StringComparison.OrdinalIgnoreCase)))
            throw new DomainRuleException("Ya existe una presentación con ese nombre.");
        var presentation = new PurchasePresentation(normalized, units);
        await repository.AddPurchasePresentationAsync(presentation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new PurchasePresentationDto(presentation.Id, presentation.Name, presentation.Units);
    }

    public async Task<IReadOnlyList<PurchaseDto>> GetPurchasesAsync(CancellationToken cancellationToken = default)
    {
        var purchases = await repository.GetPurchasesAsync(cancellationToken);
        return purchases.OrderByDescending(x => x.PurchaseDateUtc).Select(Map).ToList();
    }

    public async Task<IReadOnlyList<PurchaseDto>> GetPurchasesBySupplierAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        var purchases = await repository.GetPurchasesBySupplierAsync(supplierId, cancellationToken);
        return purchases.OrderByDescending(x => x.PurchaseDateUtc).Select(Map).ToList();
    }

    public async Task<PurchaseDto?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var purchase = await repository.GetPurchaseAsync(id, cancellationToken);
        return purchase is null ? null : Map(purchase);
    }

    public Task<bool> InvoiceExistsForSupplierAsync(Guid supplierId, string invoiceNumber, CancellationToken cancellationToken = default) =>
        supplierId == Guid.Empty || string.IsNullOrWhiteSpace(invoiceNumber)
            ? Task.FromResult(false)
            : repository.InvoiceExistsForSupplierAsync(supplierId, invoiceNumber.Trim(), cancellationToken);

    public async Task<Guid> CreatePurchaseAsync(SavePurchaseRequest request, CancellationToken cancellationToken = default)
    {
        var supplier = await repository.GetSupplierAsync(request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Proveedor no encontrado.");

        if (!supplier.IsActive)
        {
            throw new DomainRuleException("No se puede registrar una compra para un proveedor inactivo.");
        }

        var number = await repository.GeneratePurchaseNumberAsync(cancellationToken);
        var items = new List<PurchaseItem>();

        foreach (var item in request.Items)
        {
            var product = await repository.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto no encontrado.");
            items.Add(new PurchaseItem(product.Id, item.Quantity, item.UnitCost));
        }

        var purchase = new Purchase(number, supplier.Id, request.PurchaseDateUtc, currentUserAccessor.UserName, items, request.Terms, request.DueDateUtc);
        await repository.AddPurchaseAsync(purchase, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return purchase.Id;
    }

    public async Task<Guid> CreateDirectPurchaseAsync(CreateDirectPurchaseRequest request, CancellationToken cancellationToken = default)
    {
        var supplier = await repository.GetSupplierAsync(request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Proveedor no encontrado.");
        if (!supplier.IsActive)
            throw new DomainRuleException("No se puede registrar una compra para un proveedor inactivo.");
        if (string.IsNullOrWhiteSpace(request.SupplierDocumentNumber))
            throw new DomainRuleException("El número de factura o documento es requerido.");
        await EnsureInvoiceIsUniqueForSupplierAsync(request.SupplierId, request.SupplierDocumentNumber, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.SupplierNit))
            throw new DomainRuleException("El NIT de la factura es requerido.");
        if (request.SupplierDocumentDateUtc == default)
            throw new DomainRuleException("La fecha de factura o compra es requerida.");
        if (request.Terms is not (PurchaseTermsType.Cash or PurchaseTermsType.Credit))
            throw new DomainRuleException("La condición de compra no es válida.");
        if (request.Terms == PurchaseTermsType.Credit && request.CreditDays is null or <= 0)
            throw new DomainRuleException("Los días de crédito deben ser mayores a cero.");
        if (request.Items.Count == 0)
            throw new DomainRuleException("La compra debe incluir al menos un producto.");
        if (request.Items.GroupBy(x => x.ProductId).Any(group => group.Key == Guid.Empty || group.Count() > 1))
            throw new DomainRuleException("Un producto no puede repetirse en la compra.");

        var expiryDates = new Dictionary<Guid, DateOnly?>();
        var lineInputs = new List<PurchaseLineInput>();
        foreach (var requestItem in request.Items)
        {
            if (requestItem.PackageQuantity <= 0 || requestItem.PackageCost < 0 || requestItem.UnitsPerPackage <= 0)
                throw new DomainRuleException("Cada producto debe tener cantidad, empaque y costo válidos.");
            if (requestItem.PackageQuantity != decimal.Truncate(requestItem.PackageQuantity))
                throw new DomainRuleException("La cantidad de empaques debe ser un número entero.");
            var product = await repository.GetProductAsync(requestItem.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto no encontrado.");
            if (!product.IsActive)
                throw new DomainRuleException($"El producto {product.DisplayName} está inactivo.");
            if (product.ManagesExpiry && requestItem.ExpiryDate is null)
                throw new DomainRuleException($"Indique la fecha de caducidad de {product.DisplayName}.");
            lineInputs.Add(new PurchaseLineInput(product.Id, requestItem.PackageQuantity,
                requestItem.UnitsPerPackage, requestItem.PackageCost, requestItem.PackageType,
                requestItem.PresentationId, requestItem.SupplierProductName, requestItem.SupplierProductCode,
                requestItem.DiscountType, requestItem.DiscountValue, requestItem.Pricing));
            expiryDates[product.Id] = requestItem.ExpiryDate;
        }

        var purchaseItems = await BuildPurchaseItemsAsync(lineInputs, request.GlobalDiscountType,
            request.GlobalDiscountValue, cancellationToken);

        if (purchaseItems.Sum(x => x.Subtotal) <= 0)
            throw new DomainRuleException("La compra debe incluir al menos un producto con cantidad y costo válidos.");

        var dueDate = request.Terms == PurchaseTermsType.Credit
            ? request.SupplierDocumentDateUtc.AddDays(request.CreditDays!.Value)
            : (DateTimeOffset?)null;
        var purchase = new Purchase(
            await repository.GeneratePurchaseNumberAsync(cancellationToken),
            supplier.Id,
            request.SupplierDocumentDateUtc,
            currentUserAccessor.UserName,
            purchaseItems,
            request.Terms,
            dueDate,
            invoiceNumber: request.SupplierDocumentNumber,
            observations: request.Observations,
            supplierName: supplier.LegalName,
            supplierNit: request.SupplierNit,
            globalDiscountType: request.GlobalDiscountType,
            globalDiscountValue: request.GlobalDiscountValue);
        purchase.Confirm(dateTimeProvider.UtcNow, currentUserAccessor.UserName);
        await repository.AddPurchaseAsync(purchase, cancellationToken);
        await ApplyInventoryEntryAsync(
            purchase,
            expiryDates,
            BuildPricingMap(lineInputs),
            $"Compra directa / {request.SupplierDocumentNumber}.",
            null,
            cancellationToken);

        await UpdateSupplierProductsAsync(purchase, request.CreditDays, cancellationToken);

        if (request.Terms == PurchaseTermsType.Credit)
            await repository.AddPayableAccountAsync(new PayableAccount(
                purchase.Id,
                supplier.Id,
                request.SupplierDocumentNumber,
                request.SupplierDocumentDateUtc,
                dueDate!.Value,
                purchase.Total), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return purchase.Id;
    }

    public async Task<IReadOnlyList<ProductSupplierDto>> GetProductSuppliersAsync(Guid? productId = null, CancellationToken cancellationToken = default) =>
        (await repository.GetProductSuppliersAsync(productId, cancellationToken))
            .OrderBy(x => x.LastUnitCost == 0 ? decimal.MaxValue : x.LastUnitCost)
            .ThenBy(x => x.Supplier.LegalName)
            .Select(Map).ToList();

    public async Task<IReadOnlyList<SupplierProductPriceHistoryDto>> GetSupplierProductPriceHistoryAsync(Guid productId, Guid? supplierId = null, CancellationToken cancellationToken = default) =>
        (await repository.GetSupplierProductPriceHistoryAsync(productId, supplierId, cancellationToken))
            .OrderByDescending(x => x.PurchaseDateUtc).Select(Map).ToList();

    public async Task<Guid> SaveProductSupplierAsync(SaveProductSupplierRequest request, CancellationToken cancellationToken = default)
    {
        var product = await repository.GetProductAsync(request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Producto no encontrado.");
        var supplier = await repository.GetSupplierAsync(request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Proveedor no encontrado.");
        if (!product.IsActive || !supplier.IsActive)
            throw new DomainRuleException("El producto y el proveedor deben estar activos.");
        var relation = await repository.GetProductSupplierAsync(request.ProductId, request.SupplierId, cancellationToken);
        if (relation is null)
        {
            relation = new ProductSupplier(request.ProductId, request.SupplierId, request.SupplierProductName,
                request.SupplierProductCode, request.PackageType, request.UnitsPerPackage, request.Terms, request.CreditDays);
            await repository.AddProductSupplierAsync(relation, cancellationToken);
        }
        else
            relation.Update(request.SupplierProductName, request.SupplierProductCode, request.PackageType,
                request.UnitsPerPackage, request.Terms, request.CreditDays);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return relation.Id;
    }

    public async Task ConfirmPurchaseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var purchase = await repository.GetPurchaseAsync(id, cancellationToken)
            ?? throw new NotFoundException("Compra no encontrada.");

        foreach (var item in purchase.Items)
        {
            var product = await repository.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto no encontrado.");

            if (!product.IsActive) throw new DomainRuleException("No se puede confirmar una compra de un producto inactivo.");
            var stock = await repository.GetStockAsync(product.Id, cancellationToken) ?? new InventoryStock(product.Id);
            var previousStock = stock.CurrentStock;
            stock.ReplaceForSystem(previousStock + item.Quantity);

            if (await repository.GetStockAsync(product.Id, cancellationToken) is null)
            {
                await repository.AddStockAsync(stock, cancellationToken);
            }

            product.Update(
                product.InternalCode,
                product.Barcode,
                product.DisplayName,
                product.Description,
                product.CategoryId,
                product.Brand,
                product.UnitOfMeasure,
                item.UnitCost,
                product.LastCost,
                product.SalePrice,
                product.MinimumStock,
                product.AppliesTax,
                product.NoMovementDaysOverride);
            product.SetActive(product.IsActive);

            await repository.AddMovementAsync(new InventoryMovement(
                product.Id,
                InventoryMovementType.PurchaseEntry,
                item.Quantity,
                previousStock,
                stock.CurrentStock,
                dateTimeProvider.UtcNow,
                currentUserAccessor.UserName,
                purchase.Number,
                "Entrada por compra."), cancellationToken);
        }

        purchase.Confirm(dateTimeProvider.UtcNow, currentUserAccessor.UserName);
        if (purchase.Terms == PurchaseTermsType.Credit &&
            !await repository.PayableAccountExistsAsync(purchase.Id, cancellationToken))
        {
            await repository.AddPayableAccountAsync(new PayableAccount(
                purchase.Id, purchase.SupplierId, purchase.Number, purchase.PurchaseDateUtc,
                purchase.DueDateUtc!.Value, purchase.Total), cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseOrderDto>> GetPurchaseOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = await repository.GetPurchaseOrdersAsync(cancellationToken);
        var supplierProducts = await repository.GetProductSuppliersAsync(null, cancellationToken);
        return orders.OrderByDescending(x => x.RequestedAtUtc).Select(x => Map(x, supplierProducts)).ToList();
    }

    public async Task<PurchaseOrderDto?> GetPurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await repository.GetPurchaseOrderAsync(id, cancellationToken);
        return order is null ? null : Map(order, await repository.GetProductSuppliersAsync(null, cancellationToken));
    }

    public async Task<Guid> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        var supplier = await repository.GetSupplierAsync(request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Proveedor no encontrado.");
        if (!supplier.IsActive)
        {
            throw new DomainRuleException("No se puede crear una orden para un proveedor inactivo.");
        }

        if (request.Items.GroupBy(x => x.ProductId).Any(group => group.Count() > 1))
            throw new DomainRuleException("El producto ya forma parte de la orden.");

        var items = new List<PurchaseOrderItem>();
        foreach (var item in request.Items)
        {
            var product = await repository.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto no encontrado.");
            if (!product.IsActive)
            {
                throw new DomainRuleException($"El producto {product.DisplayName} está inactivo.");
            }

            if (item.PresentationId == Guid.Empty)
            {
                items.Add(new PurchaseOrderItem(product.Id, item.PackageQuantity));
                continue;
            }

            var presentation = await repository.GetPurchasePresentationAsync(item.PresentationId, cancellationToken)
                ?? throw new NotFoundException("Presentación de compra no encontrada.");
            items.Add(new PurchaseOrderItem(product.Id, item.PackageQuantity, presentation.Id,
                presentation.Name, presentation.Units));
        }

        var order = new PurchaseOrder(
            await repository.GeneratePurchaseOrderNumberAsync(cancellationToken),
            supplier.Id,
            dateTimeProvider.UtcNow,
            currentUserAccessor.UserName,
            items,
            request.Observations);
        if (request.Submit)
        {
            order.Submit(dateTimeProvider.UtcNow, currentUserAccessor.UserName);
        }
        await repository.AddPurchaseOrderAsync(order, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return order.Id;
    }

    public async Task UpdatePurchaseOrderAsync(Guid id, UpdatePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await repository.GetPurchaseOrderAsync(id, cancellationToken)
            ?? throw new NotFoundException("Orden de compra no encontrada.");
        var supplier = await repository.GetSupplierAsync(request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Proveedor no encontrado.");
        if (!supplier.IsActive)
            throw new DomainRuleException("No se puede usar un proveedor inactivo.");

        var items = await BuildOrderItemsAsync(request.Items, cancellationToken);
        order.Update(supplier.Id, items, request.Observations);
        if (request.Submit)
            order.Submit(dateTimeProvider.UtcNow, currentUserAccessor.UserName);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SubmitPurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await repository.GetPurchaseOrderAsync(id, cancellationToken)
            ?? throw new NotFoundException("Orden de compra no encontrada.");
        order.Submit(dateTimeProvider.UtcNow, currentUserAccessor.UserName);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReceivePurchaseOrderAsync(Guid id, ReceivePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await repository.GetPurchaseOrderAsync(id, cancellationToken)
            ?? throw new NotFoundException("Orden de compra no encontrada.");
        if (order.Status != PurchaseOrderStatus.Requested)
            throw new DomainRuleException("Solo una orden solicitada puede recibirse.");
        if (order.Receipt is not null || await repository.GetPurchaseByOrderAsync(id, cancellationToken) is not null)
            throw new DomainRuleException("Esta orden ya fue recibida y no puede generar otra compra.");
        if (string.IsNullOrWhiteSpace(request.SupplierDocumentNumber))
            throw new DomainRuleException("El número de factura o documento es requerido.");
        await EnsureInvoiceIsUniqueForSupplierAsync(order.SupplierId, request.SupplierDocumentNumber, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.SupplierNit))
            throw new DomainRuleException("El NIT de la factura es requerido.");
        if (request.SupplierDocumentDateUtc == default)
            throw new DomainRuleException("La fecha de factura o documento es requerida.");
        if (request.Terms is not (PurchaseTermsType.Cash or PurchaseTermsType.Credit))
            throw new DomainRuleException("La condición de compra no es válida.");
        if (request.Terms == PurchaseTermsType.Credit && request.CreditDays is null or <= 0)
            throw new DomainRuleException("Los días de crédito deben ser mayores a cero.");
        var supplier = await repository.GetSupplierAsync(order.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Proveedor no encontrado.");

        var duplicatedItem = request.Items.Where(x => !x.IsAdditional).GroupBy(x => x.ItemId).Any(x => x.Key == Guid.Empty || x.Count() > 1);
        if (duplicatedItem)
            throw new DomainRuleException("La recepción contiene líneas solicitadas inválidas o repetidas.");

        var receiptLines = new List<PurchaseReceiptItem>();
        var purchaseInputs = new List<PurchaseLineInput>();
        var receivedProductIds = order.Items.Select(x => x.ProductId).ToHashSet();
        var requestedLines = request.Items.Where(x => !x.IsAdditional).ToDictionary(x => x.ItemId);
        foreach (var orderItem in order.Items)
        {
            var requestItem = requestedLines.GetValueOrDefault(orderItem.Id)
                ?? new ReceivePurchaseOrderItemRequest(orderItem.Id, 0, 0, orderItem.ProductId);
            var packageQuantity = requestItem.PackageQuantity > 0 ? requestItem.PackageQuantity : requestItem.Quantity;
            var packageCost = requestItem.PackageQuantity > 0 ? requestItem.PackageCost : requestItem.ActualUnitCost;
            if (packageQuantity < 0 || packageCost < 0)
                throw new DomainRuleException("Las cantidades y costos reales no pueden ser negativos.");
            if (packageQuantity != decimal.Truncate(packageQuantity))
                throw new DomainRuleException("Las cantidades de empaques deben ser números enteros.");
            if (packageQuantity > 0)
                purchaseInputs.Add(new PurchaseLineInput(orderItem.ProductId, packageQuantity,
                    requestItem.UnitsPerPackage, packageCost, requestItem.PackageType, requestItem.PresentationId,
                    string.IsNullOrWhiteSpace(requestItem.SupplierProductName) ? orderItem.Product.DisplayName : requestItem.SupplierProductName,
                    requestItem.SupplierProductCode, requestItem.DiscountType, requestItem.DiscountValue,
                    requestItem.Pricing));
        }

        if (requestedLines.Keys.Any(itemId => order.Items.All(x => x.Id != itemId)))
            throw new DomainRuleException("Una línea de recepción no pertenece a la orden.");

        foreach (var requestItem in request.Items.Where(x => x.IsAdditional))
        {
            var packageQuantity = requestItem.PackageQuantity > 0 ? requestItem.PackageQuantity : requestItem.Quantity;
            var packageCost = requestItem.PackageQuantity > 0 ? requestItem.PackageCost : requestItem.ActualUnitCost;
            if (requestItem.ProductId == Guid.Empty || packageQuantity <= 0 || packageCost < 0)
                throw new DomainRuleException("Los productos adicionales deben tener producto, cantidad y costo válidos.");
            if (packageQuantity != decimal.Truncate(packageQuantity))
                throw new DomainRuleException("Las cantidades de empaques deben ser números enteros.");
            if (!receivedProductIds.Add(requestItem.ProductId))
                throw new DomainRuleException("Un producto no puede repetirse en la recepción.");
            var product = await repository.GetProductAsync(requestItem.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto adicional no encontrado.");
            if (!product.IsActive)
                throw new DomainRuleException($"El producto {product.DisplayName} está inactivo.");
            purchaseInputs.Add(new PurchaseLineInput(product.Id, packageQuantity,
                requestItem.UnitsPerPackage, packageCost, requestItem.PackageType, requestItem.PresentationId,
                string.IsNullOrWhiteSpace(requestItem.SupplierProductName) ? product.DisplayName : requestItem.SupplierProductName,
                requestItem.SupplierProductCode, requestItem.DiscountType, requestItem.DiscountValue,
                requestItem.Pricing));
        }

        var purchaseItems = await BuildPurchaseItemsAsync(purchaseInputs, request.GlobalDiscountType,
            request.GlobalDiscountValue, cancellationToken);
        var purchaseItemsByProduct = purchaseItems.ToDictionary(x => x.ProductId);
        foreach (var orderItem in order.Items)
        {
            purchaseItemsByProduct.TryGetValue(orderItem.ProductId, out var received);
            receiptLines.Add(new PurchaseReceiptItem(orderItem.ProductId, orderItem.Id, orderItem.RequestedQuantity,
                received?.Quantity ?? 0, received?.UnitCost ?? 0, false));
        }
        foreach (var requestItem in request.Items.Where(x => x.IsAdditional))
        {
            var received = purchaseItemsByProduct[requestItem.ProductId];
            receiptLines.Add(new PurchaseReceiptItem(requestItem.ProductId, null, 0,
                received.Quantity, received.UnitCost, true));
        }

        if (purchaseItems.Count == 0 || purchaseItems.Sum(x => x.Subtotal) <= 0)
            throw new DomainRuleException("La recepción debe incluir al menos un producto con cantidad y costo válidos.");

        var dueDate = request.Terms == PurchaseTermsType.Credit
            ? request.SupplierDocumentDateUtc.AddDays(request.CreditDays!.Value)
            : (DateTimeOffset?)null;
        var purchase = new Purchase(
            await repository.GeneratePurchaseNumberAsync(cancellationToken),
            order.SupplierId,
            request.SupplierDocumentDateUtc,
            currentUserAccessor.UserName,
            purchaseItems,
            request.Terms,
            dueDate,
            order.Id,
            request.SupplierDocumentNumber,
            request.Observations,
            supplier.LegalName,
            request.SupplierNit,
            request.GlobalDiscountType,
            request.GlobalDiscountValue);
        purchase.Confirm(dateTimeProvider.UtcNow, currentUserAccessor.UserName);
        await repository.AddPurchaseAsync(purchase, cancellationToken);
        var receipt = new PurchaseReceipt(
            $"REC-{order.Number}", order.Id, purchase.Id, order.SupplierId,
            request.SupplierDocumentNumber, request.SupplierDocumentDateUtc, dateTimeProvider.UtcNow,
            request.Terms, request.CreditDays, dueDate, request.Observations, receiptLines);
        await repository.AddPurchaseReceiptAsync(receipt, cancellationToken);

        var expiryDates = request.Items
            .Where(x => x.Quantity > 0)
            .ToDictionary(x => x.ProductId, x => x.ExpiryDate);
        await ApplyInventoryEntryAsync(
            purchase,
            expiryDates,
            BuildPricingMap(purchaseInputs),
            $"Recepción final {order.Number} / {request.SupplierDocumentNumber}.",
            order.Id,
            cancellationToken);

        await UpdateSupplierProductsAsync(purchase, request.CreditDays, cancellationToken);

        order.CompleteReceipt(dateTimeProvider.UtcNow, currentUserAccessor.UserName);
        order.Purchase = purchase;
        order.Receipt = receipt;

        if (request.Terms == PurchaseTermsType.Credit)
            await repository.AddPayableAccountAsync(new PayableAccount(
                purchase.Id, order.SupplierId, request.SupplierDocumentNumber,
                request.SupplierDocumentDateUtc, dueDate!.Value, purchase.Total), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureInvoiceIsUniqueForSupplierAsync(Guid supplierId, string invoiceNumber, CancellationToken cancellationToken)
    {
        var normalizedInvoiceNumber = invoiceNumber.Trim();
        if (await repository.InvoiceExistsForSupplierAsync(supplierId, normalizedInvoiceNumber, cancellationToken))
            throw new DomainRuleException($"La factura {normalizedInvoiceNumber} ya fue registrada para este proveedor.");
    }

    private async Task<List<PurchaseItem>> BuildPurchaseItemsAsync(
        IReadOnlyList<PurchaseLineInput> lines,
        PurchaseDiscountType globalDiscountType,
        decimal globalDiscountValue,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
            return [];
        if (!Enum.IsDefined(globalDiscountType) || globalDiscountValue < 0)
            throw new DomainRuleException("El descuento global no es válido.");

        var resolved = new List<ResolvedPurchaseLine>(lines.Count);
        foreach (var line in lines)
        {
            var presentation = line.PresentationId == Guid.Empty ? null
                : await repository.GetPurchasePresentationAsync(line.PresentationId, cancellationToken);
            var productPresentation = presentation is null && line.PresentationId != Guid.Empty
                ? await repository.GetProductPurchasePresentationAsync(line.PresentationId, cancellationToken) : null;
            if (line.PresentationId != Guid.Empty && presentation is null && productPresentation is null)
                throw new DomainRuleException("Seleccione una presentación válida.");
            if (presentation is { IsActive: false } || productPresentation is { IsActive: false }
                || (productPresentation is not null && productPresentation.ProductId != line.ProductId))
                throw new DomainRuleException("La presentación debe estar activa y pertenecer al producto.");
            var units = presentation?.Units ?? (productPresentation is not null ? 1 : line.UnitsPerPackage);
            var factor = productPresentation?.BaseQuantityPerPackage ?? units;
            var name = productPresentation?.Name ?? presentation?.Name ?? GetLegacyPackageName(line.PackageType);
            var product = await repository.GetProductAsync(line.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto no encontrado.");
            if (!product.IsActive) throw new DomainRuleException("El producto está inactivo.");
            if (productPresentation is not null && !product.BaseUnitId.HasValue)
                throw new DomainRuleException("El producto debe tener unidad base antes de recibir presentaciones.");
            if (line.PackageQuantity <= 0 || line.PackageQuantity != decimal.Truncate(line.PackageQuantity)
                || units <= 0 || line.PackageCost < 0)
                throw new DomainRuleException("Cada producto debe tener presentación, cantidad y precio válidos.");
            if (!Enum.IsDefined(line.DiscountType) || line.DiscountValue < 0)
                throw new DomainRuleException("El descuento de una línea no es válido.");
            ValidateProductPricing(line.Pricing);

            var gross = decimal.Round(line.PackageQuantity * line.PackageCost, 2);
            var lineDiscount = line.DiscountType switch
            {
                PurchaseDiscountType.None => 0,
                PurchaseDiscountType.Percentage when line.DiscountValue <= 100 => decimal.Round(gross * line.DiscountValue / 100m, 2),
                PurchaseDiscountType.Amount when line.DiscountValue <= gross => decimal.Round(line.DiscountValue, 2),
                _ => throw new DomainRuleException("El descuento de una línea supera el valor permitido.")
            };
            resolved.Add(new ResolvedPurchaseLine(line, units, name, gross - lineDiscount, factor,
                product.BaseUnit?.Code ?? product.UnitOfMeasure, productPresentation?.Id));
        }

        var netSubtotal = decimal.Round(resolved.Sum(x => x.NetBeforeGlobal), 2);
        var globalDiscount = globalDiscountType switch
        {
            PurchaseDiscountType.None => 0,
            PurchaseDiscountType.Percentage when globalDiscountValue <= 100 => decimal.Round(netSubtotal * globalDiscountValue / 100m, 2),
            PurchaseDiscountType.Amount when globalDiscountValue <= netSubtotal => decimal.Round(globalDiscountValue, 2),
            _ => throw new DomainRuleException("El descuento global supera el subtotal de la factura.")
        };

        var remainingDiscount = globalDiscount;
        var remainingNet = netSubtotal;
        var result = new List<PurchaseItem>(resolved.Count);
        for (var index = 0; index < resolved.Count; index++)
        {
            var current = resolved[index];
            var allocation = index == resolved.Count - 1
                ? remainingDiscount
                : remainingNet == 0 ? 0 : decimal.Round(remainingDiscount * current.NetBeforeGlobal / remainingNet, 2);
            allocation = Math.Min(allocation, current.NetBeforeGlobal);
            remainingDiscount -= allocation;
            remainingNet -= current.NetBeforeGlobal;
            var line = current.Input;
            var unitQuantity = line.PackageQuantity * current.BaseFactor;
            result.Add(new PurchaseItem(line.ProductId, unitQuantity, line.PackageCost / current.BaseFactor,
                line.PackageType, line.PackageQuantity, current.UnitsPerPackage, line.PackageCost,
                line.SupplierProductName, line.SupplierProductCode, current.PresentationName,
                line.DiscountType, line.DiscountValue, allocation, current.BaseFactor, current.BaseUnitCode, current.ProductPresentationId));
        }
        return result;
    }

    private static string GetLegacyPackageName(SupplierPackageType value) => value switch
    {
        SupplierPackageType.Box => "Caja",
        SupplierPackageType.Package => "Paquete",
        SupplierPackageType.Bundle => "Fardo",
        SupplierPackageType.Dozen => "Docena",
        SupplierPackageType.Other => "Otro",
        _ => "Unidad"
    };

    private sealed record PurchaseLineInput(Guid ProductId, decimal PackageQuantity, int UnitsPerPackage,
        decimal PackageCost, SupplierPackageType PackageType, Guid PresentationId,
        string SupplierProductName, string? SupplierProductCode, PurchaseDiscountType DiscountType,
        decimal DiscountValue, PurchaseProductPricingRequest? Pricing);

    private sealed record ResolvedPurchaseLine(PurchaseLineInput Input, int UnitsPerPackage,
        string PresentationName, decimal NetBeforeGlobal, decimal BaseFactor, string BaseUnitCode, Guid? ProductPresentationId);

    private async Task ApplyInventoryEntryAsync(
        Purchase purchase,
        IReadOnlyDictionary<Guid, DateOnly?> expiryDates,
        IReadOnlyDictionary<Guid, PurchaseProductPricingRequest> pricingByProduct,
        string movementNote,
        Guid? purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var occurredAtUtc = dateTimeProvider.UtcNow;
        foreach (var item in purchase.Items)
        {
            var product = await repository.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto no encontrado.");
            var stock = await repository.GetStockAsync(item.ProductId, cancellationToken);
            var isNewStock = stock is null;
            stock ??= new InventoryStock(item.ProductId);
            var previousStock = stock.CurrentStock;
            stock.ReplaceForSystem(previousStock + item.Quantity);
            if (isNewStock)
                await repository.AddStockAsync(stock, cancellationToken);

            product.Update(product.InternalCode, product.Barcode, product.Name, product.Description,
                product.CategoryId, product.Brand, product.UnitOfMeasure, item.UnitCost, product.LastCost,
                pricingByProduct.GetValueOrDefault(product.Id)?.SalePrice ?? product.SalePrice,
                product.MinimumStock, product.AppliesTax, product.NoMovementDaysOverride);

            if (pricingByProduct.GetValueOrDefault(product.Id) is { } pricing)
            {
                product.ConfigureMultiPricing(pricing.MultiPriceEnabled, MultiPriceMode.MinimumQuantity);
                product.SynchronizePriceRules(pricing.MultiPriceEnabled
                    ? pricing.PriceRules.Select(x => new ProductPriceRuleValue(null, x.Quantity, x.Price, true))
                    : []);
            }

            var movement = new InventoryMovement(
                item.ProductId,
                InventoryMovementType.PurchaseEntry,
                item.Quantity,
                previousStock,
                stock.CurrentStock,
                occurredAtUtc,
                currentUserAccessor.UserName,
                purchase.Number,
                movementNote,
                purchaseOrderId: purchaseOrderId,
                purchaseId: purchase.Id);

            expiryDates.TryGetValue(product.Id, out var expiryDate);
            if (product.ManagesExpiry && expiryDate is null)
                throw new DomainRuleException($"Indique la fecha de caducidad de {product.DisplayName}.");
            movement.SetLotDetails(expiryDate);
            await repository.AddMovementAsync(movement, cancellationToken);
        }
    }

    private static IReadOnlyDictionary<Guid, PurchaseProductPricingRequest> BuildPricingMap(
        IEnumerable<PurchaseLineInput> lines) =>
        lines.Where(x => x.Pricing is not null)
            .ToDictionary(x => x.ProductId, x => x.Pricing!);

    private static void ValidateProductPricing(PurchaseProductPricingRequest? pricing)
    {
        if (pricing is null) return;
        if (pricing.SalePrice < 0)
            throw new DomainRuleException("El precio de venta no puede ser negativo.");
        if (!pricing.MultiPriceEnabled) return;
        if (pricing.PriceRules.Count == 0)
            throw new DomainRuleException("Agregue al menos un multiprecio o desactive la opción.");
        if (pricing.PriceRules.Any(x => x.Quantity <= 0 || x.Price < 0))
            throw new DomainRuleException("Las cantidades y precios de multiprecio deben ser válidos.");
        if (pricing.PriceRules.GroupBy(x => decimal.Round(x.Quantity, 2)).Any(x => x.Count() > 1))
            throw new DomainRuleException("No se permiten cantidades repetidas en los multiprecios.");
    }

    private async Task UpdateSupplierProductsAsync(Purchase purchase, int? creditDays, CancellationToken cancellationToken)
    {
        foreach (var item in purchase.Items)
        {
            var product = await repository.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto no encontrado.");
            var relation = await repository.GetProductSupplierAsync(item.ProductId, purchase.SupplierId, cancellationToken);
            if (relation is null)
            {
                relation = new ProductSupplier(item.ProductId, purchase.SupplierId,
                    item.SupplierProductName ?? product.DisplayName, item.SupplierProductCode,
                    item.PackageType, item.UnitsPerPackage, purchase.Terms, creditDays);
                await repository.AddProductSupplierAsync(relation, cancellationToken);
            }
            else
                relation.Update(item.SupplierProductName ?? product.DisplayName, item.SupplierProductCode,
                    item.PackageType, item.UnitsPerPackage, purchase.Terms, creditDays);

            relation.SetPackageName(item.PackageName);
            relation.RegisterPurchase(item.UnitCost * item.UnitsPerPackage, purchase.PurchaseDateUtc, purchase.Terms, creditDays);
            await repository.AddSupplierProductPriceHistoryAsync(new SupplierProductPriceHistory(
                relation.Id, item.ProductId, purchase.SupplierId, purchase.Id, purchase.PurchaseDateUtc,
                item.PackageType, item.PackageQuantity, item.UnitsPerPackage, item.PackageCost,
                item.UnitCost, item.PackageName, item.LineDiscountAmount, item.GlobalDiscountAmount), cancellationToken);
        }
    }

    public async Task CancelPurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await repository.GetPurchaseOrderAsync(id, cancellationToken)
            ?? throw new NotFoundException("Orden de compra no encontrada.");
        order.Cancel();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static PurchaseDto Map(Purchase purchase)
    {
        var traceability = new List<PurchaseTraceStageDto>();
        if (purchase.PurchaseOrder is { } order)
        {
            traceability.Add(new("Borrador creado", order.CreatedBy ?? order.UserName, order.CreatedAtUtc == default ? order.RequestedAtUtc : order.CreatedAtUtc));
            if (order.SubmittedAtUtc.HasValue)
                traceability.Add(new("Orden solicitada", order.SubmittedBy ?? order.UserName, order.SubmittedAtUtc.Value));
            if (order.ModifiedAtUtc.HasValue && order.ModifiedAtUtc.Value != order.CreatedAtUtc)
                traceability.Add(new("Orden modificada", order.ModifiedBy ?? order.UserName, order.ModifiedAtUtc.Value));
            if (order.ReceivedAtUtc.HasValue)
                traceability.Add(new("Recepción realizada", order.ReceivedBy ?? purchase.UserName, order.ReceivedAtUtc.Value));
        }
        traceability.Add(new("Compra registrada/confirmada", purchase.ConfirmedBy ?? purchase.CreatedBy ?? purchase.UserName,
            purchase.ConfirmedAtUtc ?? (purchase.CreatedAtUtc == default ? purchase.PurchaseDateUtc : purchase.CreatedAtUtc)));
        return new(
            purchase.Id,
            purchase.Number,
            purchase.SupplierId,
            purchase.SupplierName ?? purchase.Supplier.LegalName,
            purchase.PurchaseDateUtc,
            purchase.UserName,
            purchase.Status,
            purchase.Total,
            purchase.Items.Select(x => new PurchaseItemDto(x.ProductId, x.Product.DisplayName, x.Quantity, x.UnitCost,
                x.Subtotal, x.SupplierProductName, x.Product.Description, null, x.PackageType,
                x.PackageQuantity, x.UnitsPerPackage, x.PackageCost, x.SupplierProductCode,
                x.PackageName, x.GrossSubtotal, x.LineDiscountType, x.LineDiscountValue,
                x.LineDiscountAmount, x.GlobalDiscountAmount, x.BaseQuantityPerPackage, x.BaseUnitCode)).ToList(),
            purchase.InvoiceNumber, purchase.Terms, purchase.DueDateUtc, purchase.PurchaseOrderId, purchase.Observations,
            purchase.PurchaseOrder?.Number, traceability, purchase.SupplierNit ?? purchase.Supplier.Nit,
            purchase.GrossSubtotal, purchase.LineDiscountTotal, purchase.NetSubtotal,
            purchase.GlobalDiscountType, purchase.GlobalDiscountValue, purchase.GlobalDiscountAmount);
    }

    private PurchaseOrderDto Map(PurchaseOrder order, IReadOnlyList<ProductSupplier> supplierProducts)
    {
        var elapsedDays = Math.Max(0, (dateTimeProvider.UtcNow.Date - order.RequestedAtUtc.UtcDateTime.Date).Days);
        var receiptByOrderItem = order.Receipt?.Items.Where(x => x.PurchaseOrderItemId.HasValue)
            .ToDictionary(x => x.PurchaseOrderItemId!.Value) ?? [];
        var items = order.Items.Select(x =>
        {
            var received = receiptByOrderItem.GetValueOrDefault(x.Id);
            var relation = supplierProducts.FirstOrDefault(value => value.ProductId == x.ProductId && value.SupplierId == order.SupplierId);
            return new PurchaseOrderItemDto(x.Id, x.ProductId, x.Product.DisplayName, x.RequestedQuantity,
                received?.ReceivedQuantity ?? 0, Math.Max(0, x.RequestedQuantity - (received?.ReceivedQuantity ?? 0)),
                relation?.SupplierProductName, x.Product.Description, null,
                received?.ActualUnitCost ?? 0, false, received?.Subtotal ?? 0,
                x.PresentationId, x.PresentationName, x.PackageQuantity, x.UnitsPerPackage);
        }).ToList();
        if (order.Receipt is not null)
        {
            items.AddRange(order.Receipt.Items.Where(x => x.IsAdditional).Select(x => new PurchaseOrderItemDto(
                x.Id, x.ProductId, x.Product.DisplayName, 0, x.ReceivedQuantity, 0,
                supplierProducts.FirstOrDefault(value => value.ProductId == x.ProductId && value.SupplierId == order.SupplierId)?.SupplierProductName,
                x.Product.Description, null,
                x.ActualUnitCost, true, x.Subtotal)));
        }
        return new PurchaseOrderDto(
            order.Id,
            order.Number,
            order.SupplierId,
            order.Supplier.TradeName ?? order.Supplier.LegalName,
            order.RequestedAtUtc,
            order.UserName,
            order.Status,
            elapsedDays,
            items,
            order.SubmittedAtUtc,
            order.ReceivedAtUtc,
            order.Observations,
            order.Receipt?.Number,
            order.Receipt?.SupplierDocumentNumber,
            order.Receipt?.SupplierDocumentDateUtc,
            order.Receipt?.Terms,
            order.Receipt?.CreditDays,
            order.Receipt?.DueDateUtc,
            order.Receipt?.Observations,
            order.Receipt?.Total ?? 0,
            order.Purchase?.Id,
            order.SubmittedBy,
            order.ReceivedBy);
    }

    private async Task<List<PurchaseOrderItem>> BuildOrderItemsAsync(
        IReadOnlyList<PurchaseOrderItemRequest> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
            throw new DomainRuleException("La orden debe incluir al menos un producto.");
        if (requests.GroupBy(x => x.ProductId).Any(group => group.Key == Guid.Empty || group.Count() > 1))
            throw new DomainRuleException("El producto ya forma parte de la orden.");
        var items = new List<PurchaseOrderItem>();
        foreach (var item in requests)
        {
            var product = await repository.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto no encontrado.");
            if (!product.IsActive)
                throw new DomainRuleException($"El producto {product.DisplayName} está inactivo.");
            if (item.PresentationId == Guid.Empty)
            {
                items.Add(new PurchaseOrderItem(product.Id, item.PackageQuantity));
                continue;
            }

            var presentation = await repository.GetPurchasePresentationAsync(item.PresentationId, cancellationToken)
                ?? throw new NotFoundException("Presentación de compra no encontrada.");
            items.Add(new PurchaseOrderItem(product.Id, item.PackageQuantity, presentation.Id,
                presentation.Name, presentation.Units));
        }
        return items;
    }

    private static ProductSupplierDto Map(ProductSupplier item) => new(
        item.Id, item.ProductId, item.Product.DisplayName, item.SupplierId,
        item.Supplier.TradeName ?? item.Supplier.LegalName, item.SupplierProductName,
        item.SupplierProductCode, item.PackageType, item.UnitsPerPackage,
        item.LastPackageCost, item.LastUnitCost, item.LastPurchaseDateUtc, item.Terms, item.CreditDays,
        item.PackageName);

    private static SupplierProductPriceHistoryDto Map(SupplierProductPriceHistory item) => new(
        item.Id, item.ProductId, item.Product.DisplayName, item.SupplierId,
        item.Supplier.TradeName ?? item.Supplier.LegalName, item.PurchaseDateUtc,
        item.PackageType, item.PackageQuantity, item.UnitsPerPackage,
        item.PackageCost, item.UnitCost, item.TotalCost, item.PackageName,
        item.LineDiscountAmount, item.GlobalDiscountAmount);
}
