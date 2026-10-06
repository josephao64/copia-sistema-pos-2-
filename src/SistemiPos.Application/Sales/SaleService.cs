using SistemiPos.Application.Abstractions;
using SistemiPos.Application.Catalog;
using SistemiPos.Application.Common.Exceptions;
using SistemiPos.Application.Security;
using SistemiPos.Domain.Cash;
using SistemiPos.Domain.Common.Exceptions;
using SistemiPos.Domain.Configuration;
using SistemiPos.Domain.Customers;
using SistemiPos.Domain.Inventory;
using SistemiPos.Domain.Finance;
using SistemiPos.Domain.Sales;

namespace SistemiPos.Application.Sales;

public sealed class SaleService(
    ISaleRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    IDateTimeProvider dateTimeProvider,
    IProductPricingService? productPricingService = null,
    IOperationAuthorizationService? operationAuthorizationService = null) : ISaleService
{
    private readonly IProductPricingService _productPricingService = productPricingService ?? ProductPricingService.Instance;

    public async Task<IReadOnlyList<SaleDto>> GetSalesAsync(CancellationToken cancellationToken = default)
    {
        var sales = await repository.GetSalesAsync(cancellationToken);
        return sales.OrderByDescending(x => x.SoldAtUtc).Select(Map).ToList();
    }

    public async Task<SaleDto?> GetSaleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sale = await repository.GetSaleAsync(id, cancellationToken);
        return sale is null ? null : Map(sale);
    }

    public async Task<Guid> CreateSaleAsync(CreateSaleRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.ClientOperationId)
            && await repository.ExistsClientOperationIdAsync(request.ClientOperationId, cancellationToken))
        {
            var existingSale = (await repository.GetSalesAsync(cancellationToken))
                .FirstOrDefault(x => string.Equals(x.ClientOperationId, request.ClientOperationId.Trim(), StringComparison.Ordinal));
            if (existingSale is null
                || existingSale.CashSessionId != request.CashSessionId
                || !string.Equals(existingSale.CashierUserName, currentUserAccessor.UserName, StringComparison.OrdinalIgnoreCase))
                throw new DomainRuleException("El identificador de la operación offline ya fue utilizado por otra venta.");
            return existingSale.Id;
        }

        var settings = await repository.GetSystemSettingAsync(cancellationToken);
        var cashSession = await repository.GetCashSessionAsync(request.CashSessionId, cancellationToken)
            ?? throw new NotFoundException("La sesión de caja no fue encontrada.");

        if (cashSession.Status != CashSessionStatus.Active)
        {
            throw new DomainRuleException("La venta requiere una caja activa.");
        }
        if (!string.Equals(cashSession.CashierUserName, currentUserAccessor.UserName, StringComparison.OrdinalIgnoreCase))
            throw new DomainRuleException("La venta debe registrarse en la caja abierta del usuario actual.");
        if (request.Payment.PaymentMethod == PaymentMethodType.Pos
            && cashSession.AssignedPosDevices.Count > 0
            && cashSession.AssignedPosDevices.All(x => x.PosDeviceId != request.Payment.PosDeviceId))
            throw new DomainRuleException("El POS seleccionado no está asignado a la caja actual.");

        var customer = await ResolveCustomerAsync(request, cancellationToken);
        var customerId = customer?.Id;

        var userCanDiscount = await repository.UserHasPermissionAsync(currentUserAccessor.UserName, PermissionCodes.DiscountsApply, cancellationToken);
        var lineDiscountAudits = new List<(string ProductName, SaleDiscountType Type, decimal Value,
            decimal Amount, string? AuthorizedBy, DateTimeOffset? AuthorizedAt, string? Comment)>();

        var items = new List<SaleItem>();
        var requestedByProduct = new Dictionary<Guid, decimal>();
        foreach (var item in request.Items)
        {
            var product = await repository.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto no encontrado.");

            if (product.ArticleType == SistemiPos.Domain.Catalog.ArticleType.Ingrediente)
                throw new DomainRuleException("Los ingredientes se consumen mediante recetas y no se venden directamente.");

            if (!product.IsActive)
            {
                throw new DomainRuleException("No se puede vender un producto inactivo.");
            }

            var stock = await repository.GetStockAsync(product.Id, cancellationToken)
                ?? throw new NotFoundException("Existencia no encontrada para el producto.");

            var requestedQuantity = requestedByProduct.GetValueOrDefault(product.Id) + item.Quantity;
            if (stock.CurrentStock < requestedQuantity)
            {
                throw new DomainRuleException("No hay existencia suficiente para confirmar la venta.");
            }
            requestedByProduct[product.Id] = requestedQuantity;

            var price = _productPricingService.Calculate(product, item.Quantity);
            var discountType = item.DiscountValue.HasValue
                ? item.DiscountType
                : item.DiscountPercent > 0 ? SaleDiscountType.Percentage : SaleDiscountType.None;
            var discountValue = item.DiscountValue ?? item.DiscountPercent;
            if (!Enum.IsDefined(discountType))
                throw new DomainRuleException("El tipo de descuento no es válido.");
            if (discountValue < 0)
                throw new DomainRuleException("El descuento no puede ser negativo.");

            var discountPercent = discountType == SaleDiscountType.Percentage ? discountValue : 0m;
            decimal? fixedDiscountAmount = discountType == SaleDiscountType.FixedAmount ? discountValue : null;
            if (discountType == SaleDiscountType.None && discountValue != 0)
                throw new DomainRuleException("El tipo de descuento no es válido.");
            if (discountPercent > 100m)
                throw new DomainRuleException("El descuento no puede superar el total de la línea.");

            var requestedDiscountAmount = fixedDiscountAmount
                ?? decimal.Round(price.GrossAmount * discountPercent / 100m, 2);
            if (requestedDiscountAmount > price.GrossAmount)
                throw new DomainRuleException("El descuento no puede superar el total de la línea.");

            if (requestedDiscountAmount > 0)
            {
                if (!settings.DiscountsEnabled || !userCanDiscount)
                    throw new DomainRuleException("El usuario no tiene permiso para aplicar descuentos.");

            }

            var saleItem = new SaleItem(
                product.Id,
                product.DisplayName,
                item.Quantity,
                price.UnitPrice,
                discountPercent,
                settings.TaxPercent,
                !product.AppliesTax,
                product.LastCost,
                price.GrossAmount,
                fixedDiscountAmount);
            items.Add(saleItem);

            if (saleItem.DiscountAmount > 0)
                lineDiscountAudits.Add((product.DisplayName, discountType, discountValue,
                    saleItem.DiscountAmount, item.DiscountAuthorizedByUserName, item.DiscountAuthorizedAtUtc, item.DiscountComment));
        }

        DiscountApprovalRequest? discountApproval = null;
        if (items.Any(x => x.DiscountAmount > 0))
        {
            if (request.DiscountApprovalRequestId is null)
                throw new DomainRuleException("Los descuentos requieren aprobación antes de confirmar la venta.");
            discountApproval = await repository.GetDiscountApprovalAsync(request.DiscountApprovalRequestId.Value, cancellationToken)
                ?? throw new DomainRuleException("La solicitud de aprobación del descuento no existe.");
            ValidateDiscountApproval(discountApproval, cashSession.Id, currentUserAccessor.UserName, request.Items, items);
        }

        var total = decimal.Round(items.Sum(x => x.Subtotal), 2);
        var payment = await BuildPaymentAsync(request.Payment, total, settings, cancellationToken);
        var number = await repository.GenerateSaleNumberAsync(cancellationToken);
        var soldAtUtc = request.LocalCreatedAtUtc ?? dateTimeProvider.UtcNow;

        var sale = new Sale(
            number,
            cashSession.Id,
            customerId,
            soldAtUtc,
            currentUserAccessor.UserName,
            items,
            payment,
            settings.CurrencyCode,
            settings.CurrencyName,
            settings.CurrencySymbol,
            settings.CurrencyDecimals,
            settings.DecimalSeparator,
            settings.ThousandsSeparator,
            settings.CurrencySymbolPosition.ToString(),
            NormalizeCustomerNitForSnapshot(request.CustomerNit),
            FirstNotBlank(request.CustomerName, customer?.Name),
            FirstNotBlank(request.CustomerAddress, customer?.Address),
            FirstNotBlank(request.CustomerPhone, customer?.Phone),
            FirstNotBlank(request.CustomerVisitOrigin, customer?.VisitOrigin),
            request.ClientOperationId);

        if (!string.IsNullOrWhiteSpace(request.ClientOperationId))
            sale.MarkSynchronized(dateTimeProvider.UtcNow);

        string? saleDiscountAuthorizedByUserName = null;
        if (sale.DiscountTotal > 0)
        {
            DateTimeOffset? authorizedAtUtc = null;

            saleDiscountAuthorizedByUserName = discountApproval!.ResolvedByUserName;
            authorizedAtUtc = discountApproval.ResolvedAtUtc;

            sale.RegisterDiscountAudit(
                currentUserAccessor.UserName,
                dateTimeProvider.UtcNow,
                request.DiscountReason,
                saleDiscountAuthorizedByUserName,
                authorizedAtUtc);
        }

        foreach (var item in sale.Items)
        {
            var product = await repository.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto no encontrado.");
            var stock = await repository.GetStockAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Existencia no encontrada para el producto.");
            var previous = stock.CurrentStock;

            stock.ReplaceForSystem(previous - item.Quantity);
            await repository.AddMovementAsync(new InventoryMovement(
                item.ProductId,
                InventoryMovementType.SaleExit,
                item.Quantity,
                previous,
                stock.CurrentStock,
                dateTimeProvider.UtcNow,
                currentUserAccessor.UserName,
                sale.Number,
                "Salida por venta."), cancellationToken);
        }

        var cashMovement = CreateCashMovement(sale);
        if (cashMovement is not null)
        {
            cashMovement.CashSessionId = cashSession.Id;
            cashSession.AddMovement(cashMovement);
            await repository.AddCashMovementAsync(cashMovement, cancellationToken);
        }
        await repository.AddSaleAsync(sale, cancellationToken);
        discountApproval?.MarkUsed(sale.Id);
        foreach (var discount in lineDiscountAudits)
        {
            var typeLabel = discount.Type == SaleDiscountType.FixedAmount ? "Monto fijo" : "Porcentaje";
            var valueLabel = discount.Type == SaleDiscountType.FixedAmount
                ? $"{settings.CurrencySymbol} {discount.Value:N2}"
                : $"{discount.Value:N2}%";
            await repository.AddFinancialAuditAsync(new SistemiPos.Domain.Audit.FinancialAuditEntry
            {
                OccurredAtUtc = discount.AuthorizedAt ?? dateTimeProvider.UtcNow,
                UserName = currentUserAccessor.UserName,
                Action = "Descuento por producto",
                CashSessionId = cashSession.Id,
                DocumentType = nameof(Sale),
                DocumentId = sale.Id,
                DocumentReference = sale.Number,
                Amount = discount.Amount,
                AuthorizedByUserName = discount.AuthorizedBy ?? saleDiscountAuthorizedByUserName,
                Comment = $"Producto: {discount.ProductName}. Tipo: {typeLabel}. Valor: {valueLabel}. Monto descontado: {settings.CurrencySymbol} {discount.Amount:N2}." +
                    (string.IsNullOrWhiteSpace(discount.Comment) ? string.Empty : $" Comentario: {discount.Comment.Trim()}")
            }, cancellationToken);
        }
        if (sale.Payment.PaymentMethod == PaymentMethodType.Credit)
        {
            if (sale.CustomerId is null || customer is null) throw new DomainRuleException("Una venta a crédito requiere un cliente registrado.");
            var dueDateUtc = soldAtUtc.AddDays(customer.CreditDays);
            await repository.AddReceivableAccountAsync(new ReceivableAccount(
                sale.Id, sale.CustomerId.Value, sale.Number, soldAtUtc, dueDateUtc, sale.Total,
                customer.CreditDays, customer.GraceDays), cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return sale.Id;
    }

    public async Task<Guid> RequestDiscountApprovalAsync(Guid cashSessionId, IReadOnlyList<SaleItemRequest> requestedItems, CancellationToken cancellationToken = default)
    {
        var session = await repository.GetCashSessionAsync(cashSessionId, cancellationToken)
            ?? throw new NotFoundException("La sesión de caja no fue encontrada.");
        if (session.Status != CashSessionStatus.Active || !string.Equals(session.CashierUserName, currentUserAccessor.UserName, StringComparison.OrdinalIgnoreCase))
            throw new DomainRuleException("La solicitud debe pertenecer a la caja activa del vendedor.");
        var settings = await repository.GetSystemSettingAsync(cancellationToken);
        if (!settings.DiscountsEnabled || !await repository.UserHasPermissionAsync(currentUserAccessor.UserName, PermissionCodes.DiscountsApply, cancellationToken))
            throw new DomainRuleException("El usuario no tiene permiso para aplicar descuentos.");

        var snapshots = new List<DiscountApprovalItem>();
        foreach (var item in requestedItems.Where(x => x.Quantity > 0))
        {
            var product = await repository.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Producto no encontrado.");
            var price = _productPricingService.Calculate(product, item.Quantity);
            var type = item.DiscountValue.HasValue ? item.DiscountType : item.DiscountPercent > 0 ? SaleDiscountType.Percentage : SaleDiscountType.None;
            var value = item.DiscountValue ?? item.DiscountPercent;
            var amount = type == SaleDiscountType.FixedAmount ? value : decimal.Round(price.GrossAmount * value / 100m, 2);
            if (value < 0 || amount > price.GrossAmount || (type == SaleDiscountType.Percentage && value > 100m))
                throw new DomainRuleException("El descuento no puede superar el total de la línea.");
            snapshots.Add(new DiscountApprovalItem(product.Id, product.DisplayName, item.Quantity, price.UnitPrice,
                type, value, price.GrossAmount, Math.Max(0, amount), item.DiscountComment));
        }

        var approval = new DiscountApprovalRequest(cashSessionId, currentUserAccessor.UserName, dateTimeProvider.UtcNow, snapshots);
        await repository.AddDiscountApprovalAsync(approval, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return approval.Id;
    }

    public async Task<DiscountApprovalDto?> GetDiscountApprovalAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var approval = await repository.GetDiscountApprovalAsync(id, cancellationToken);
        if (approval is null) return null;
        var canAuthorize = await repository.UserHasPermissionAsync(currentUserAccessor.UserName, PermissionCodes.DiscountsAuthorize, cancellationToken);
        if (!canAuthorize && !string.Equals(approval.RequestedByUserName, currentUserAccessor.UserName, StringComparison.OrdinalIgnoreCase))
            throw new DomainRuleException("No tiene permiso para consultar esta solicitud.");
        return MapApproval(approval);
    }

    public async Task<IReadOnlyList<DiscountApprovalDto>> GetPendingDiscountApprovalsAsync(CancellationToken cancellationToken = default)
    {
        if (!await repository.UserHasPermissionAsync(currentUserAccessor.UserName, PermissionCodes.DiscountsAuthorize, cancellationToken))
            throw new DomainRuleException("No tiene permiso para autorizar descuentos.");
        return (await repository.GetPendingDiscountApprovalsAsync(cancellationToken)).Select(MapApproval).ToList();
    }

    public async Task DecideDiscountApprovalAsync(Guid id, bool approve, string? comment, CancellationToken cancellationToken = default)
    {
        if (!await repository.UserHasPermissionAsync(currentUserAccessor.UserName, PermissionCodes.DiscountsAuthorize, cancellationToken))
            throw new DomainRuleException("No tiene permiso para autorizar descuentos.");
        var approval = await repository.GetDiscountApprovalAsync(id, cancellationToken)
            ?? throw new NotFoundException("Solicitud de descuento no encontrada.");
        if (string.Equals(approval.RequestedByUserName, currentUserAccessor.UserName, StringComparison.OrdinalIgnoreCase))
            throw new DomainRuleException("La solicitud debe ser resuelta por un usuario superior distinto al vendedor.");
        if (approve) approval.Approve(currentUserAccessor.UserName, dateTimeProvider.UtcNow, comment);
        else approval.Reject(currentUserAccessor.UserName, dateTimeProvider.UtcNow, comment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateDiscountApproval(DiscountApprovalRequest approval, Guid cashSessionId, string seller,
        IReadOnlyList<SaleItemRequest> requests, IReadOnlyList<SaleItem> saleItems)
    {
        if (approval.Status != DiscountApprovalStatus.Approved)
            throw new DomainRuleException(approval.Status == DiscountApprovalStatus.Rejected
                ? $"El descuento fue rechazado{(string.IsNullOrWhiteSpace(approval.DecisionComment) ? "." : $": {approval.DecisionComment}")}" : "El descuento todavía está pendiente de aprobación.");
        if (approval.CashSessionId != cashSessionId || !string.Equals(approval.RequestedByUserName, seller, StringComparison.OrdinalIgnoreCase))
            throw new DomainRuleException("La aprobación no corresponde a esta caja y vendedor.");

        var expected = requests.Zip(saleItems).Select(pair => (pair.First.ProductId, pair.First.Quantity,
            pair.First.DiscountValue.HasValue ? pair.First.DiscountType : pair.First.DiscountPercent > 0 ? SaleDiscountType.Percentage : SaleDiscountType.None,
            pair.First.DiscountValue ?? pair.First.DiscountPercent, pair.Second.DiscountAmount))
            .OrderBy(x => x.ProductId).ThenBy(x => x.Quantity).ThenBy(x => x.Item3).ThenBy(x => x.Item4).ToList();
        var approved = approval.Items.Select(x => (x.ProductId, x.Quantity, x.DiscountType, x.DiscountValue, x.DiscountAmount))
            .OrderBy(x => x.ProductId).ThenBy(x => x.Quantity).ThenBy(x => x.DiscountType).ThenBy(x => x.DiscountValue).ToList();
        if (!expected.SequenceEqual(approved))
            throw new DomainRuleException("El carrito cambió después de solicitar la aprobación. Envíe una nueva solicitud.");
    }

    private static DiscountApprovalDto MapApproval(DiscountApprovalRequest x) => new(x.Id, x.CashSessionId,
        x.RequestedByUserName, x.RequestedAtUtc, x.Status, x.ResolvedByUserName, x.ResolvedAtUtc,
        x.DecisionComment, x.GrossTotal, x.DiscountTotal, x.NetTotal,
        x.Items.Select(i => new DiscountApprovalItemDto(i.ProductId, i.ProductName, i.Quantity, i.UnitPrice,
            i.DiscountType, i.DiscountValue, i.GrossAmount, i.DiscountAmount, i.Comment)).ToList());

    public async Task CancelSaleAsync(Guid saleId, CancelSaleRequest request, CancellationToken cancellationToken = default)
    {
        var authorizedBy = await ResolveSensitiveAuthorizationAsync(PermissionCodes.SalesCancelWithoutAuthorization,
            PermissionCodes.SalesCancelAuthorize, request.AuthorizerUserName, request.AuthorizerPassword, cancellationToken);
        var sale = await repository.GetSaleAsync(saleId, cancellationToken)
            ?? throw new NotFoundException("Venta no encontrada.");

        sale.Cancel();

        foreach (var item in sale.Items)
        {
            var stock = await repository.GetStockAsync(item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Existencia no encontrada para el producto.");
            var previous = stock.CurrentStock;
            stock.ReplaceForSystem(previous + item.Quantity);

            await repository.AddMovementAsync(new InventoryMovement(
                item.ProductId,
                InventoryMovementType.CancellationReversal,
                item.Quantity,
                previous,
                stock.CurrentStock,
                dateTimeProvider.UtcNow,
                currentUserAccessor.UserName,
                sale.Number,
                "Reversión por anulación."), cancellationToken);
        }

        var cancellationNumber = await repository.GenerateCancellationNumberAsync(cancellationToken);
        await repository.AddCancellationAsync(new SaleCancellation(cancellationNumber, sale.Id, request.Reason, currentUserAccessor.UserName, dateTimeProvider.UtcNow, authorizedBy), cancellationToken);
        Guid? affectedSessionId = null;
        if (sale.Payment.PaymentMethod == PaymentMethodType.Cash)
        {
            var currentSession = await repository.GetActiveCashSessionByUserAsync(currentUserAccessor.UserName, cancellationToken)
                ?? throw new DomainRuleException("Debe abrir su caja para devolver el efectivo de la anulación.");
            var refund = new CashMovement(CashMovementType.RefundCash, sale.Total, $"Anulación {sale.Number}", currentUserAccessor.UserName,
                dateTimeProvider.UtcNow, authorizedBy, nameof(SaleCancellation), sale.Id, cancellationNumber) { CashSessionId = currentSession.Id };
            currentSession.AddMovement(refund); affectedSessionId = currentSession.Id;
        }
        else if (sale.Payment.PaymentMethod == PaymentMethodType.Credit)
        {
            var receivable = await repository.GetReceivableAccountBySaleAsync(sale.Id, cancellationToken);
            receivable?.Cancel();
        }
        await repository.AddFinancialAuditAsync(new SistemiPos.Domain.Audit.FinancialAuditEntry
        {
            OccurredAtUtc = dateTimeProvider.UtcNow, UserName = currentUserAccessor.UserName, Action = "Anulación de venta",
            CashSessionId = affectedSessionId, DocumentType = nameof(Sale), DocumentId = sale.Id, DocumentReference = sale.Number,
            Amount = sale.Total, AuthorizedByUserName = authorizedBy, Comment = request.Reason
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReturnSaleAsync(Guid saleId, ReturnSaleRequest request, CancellationToken cancellationToken = default)
    {
        var authorizedBy = await ResolveSensitiveAuthorizationAsync(PermissionCodes.SalesReturnWithoutAuthorization,
            PermissionCodes.SalesReturnAuthorize, request.AuthorizerUserName, request.AuthorizerPassword, cancellationToken);
        var sale = await repository.GetSaleAsync(saleId, cancellationToken)
            ?? throw new NotFoundException("Venta no encontrada.");

        var saleItemsByProduct = sale.Items.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var returnedByProduct = await repository.GetReturnedQuantitiesByProductAsync(sale.Id, cancellationToken);
        var requestedReturnsByProduct = new Dictionary<Guid, decimal>();

        foreach (var item in request.Items)
        {
            if (!saleItemsByProduct.TryGetValue(item.ProductId, out var soldQuantity))
            {
                throw new DomainRuleException("La devolución excede la cantidad vendida.");
            }

            var alreadyReturned = returnedByProduct.GetValueOrDefault(item.ProductId);
            var requestedReturn = requestedReturnsByProduct.GetValueOrDefault(item.ProductId) + item.Quantity;
            if (alreadyReturned + requestedReturn > soldQuantity)
            {
                throw new DomainRuleException("La devolución excede la cantidad vendida.");
            }
            requestedReturnsByProduct[item.ProductId] = requestedReturn;

            if (item.ReturnedToInventory)
            {
                var stock = await repository.GetStockAsync(item.ProductId, cancellationToken)
                    ?? throw new NotFoundException("Existencia no encontrada para el producto.");
                var previous = stock.CurrentStock;
                stock.ReplaceForSystem(previous + item.Quantity);

                var movement = new InventoryMovement(
                    item.ProductId,
                    InventoryMovementType.ReturnEntry,
                    item.Quantity,
                    previous,
                    stock.CurrentStock,
                    dateTimeProvider.UtcNow,
                    currentUserAccessor.UserName,
                    sale.Number,
                    "Entrada por devolución.");
                movement.SetLotDetails(null, selectedLotId: item.LotId);
                await repository.AddMovementAsync(movement, cancellationToken);
            }
        }

        var returnNumber = await repository.GenerateReturnNumberAsync(cancellationToken);
        var saleReturn = new SaleReturn(
            returnNumber,
            sale.Id,
            request.Reason,
            currentUserAccessor.UserName,
            dateTimeProvider.UtcNow,
            request.Items.Select(x => new SaleReturnItem(x.ProductId, x.Quantity, x.ReturnedToInventory)),
            authorizedBy);

        var totalReturned = returnedByProduct.Values.Sum() + request.Items.Sum(x => x.Quantity);
        if (totalReturned >= sale.Items.Sum(x => x.Quantity))
        {
            sale.MarkReturned();
        }
        else
        {
            sale.MarkPartiallyReturned();
        }

        await repository.AddReturnAsync(saleReturn, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RefundSaleAsync(Guid saleId, RefundSaleRequest request, CancellationToken cancellationToken = default)
    {
        var sale = await repository.GetSaleAsync(saleId, cancellationToken)
            ?? throw new NotFoundException("Venta no encontrada.");

        var refundedTotal = await repository.GetRefundedTotalAsync(sale.Id, cancellationToken);
        if (refundedTotal + request.Amount > sale.Total)
        {
            throw new DomainRuleException("El reembolso excede el total pendiente de la venta.");
        }

        var refundAuthorizedBy = operationAuthorizationService is null
            ? request.AuthorizedByUserName
            : await operationAuthorizationService.ValidateCredentialsAsync(request.AuthorizedByUserName, request.AuthorizerPassword ?? string.Empty,
                PermissionCodes.SalesReturnAuthorize, cancellationToken);

        var cashSession = await repository.GetCashSessionAsync(request.CashSessionId, cancellationToken)
            ?? throw new NotFoundException("La sesión de caja no fue encontrada.");

        if (cashSession.Status != CashSessionStatus.Active)
        {
            throw new DomainRuleException("El reembolso requiere una caja activa.");
        }
        if (!string.Equals(cashSession.CashierUserName, currentUserAccessor.UserName, StringComparison.OrdinalIgnoreCase))
            throw new DomainRuleException("El reembolso debe afectar la caja abierta del usuario que lo realiza.");

        var refund = new SaleRefund(
            sale.Id,
            cashSession.Id,
            request.Amount,
            request.RefundMethod,
            request.Reason,
            currentUserAccessor.UserName,
            refundAuthorizedBy,
            dateTimeProvider.UtcNow,
            request.Reference);

        cashSession.AddMovement(request.RefundMethod switch
        {
            RefundMethodType.Cash => new CashMovement(CashMovementType.RefundCash, request.Amount, $"Reembolso {sale.Number}", currentUserAccessor.UserName, dateTimeProvider.UtcNow),
            RefundMethodType.Transfer => new CashMovement(CashMovementType.RefundTransfer, request.Amount, $"Reembolso {sale.Number}", currentUserAccessor.UserName, dateTimeProvider.UtcNow),
            RefundMethodType.Pos => new CashMovement(CashMovementType.RefundPos, request.Amount, $"Reembolso {sale.Number}", currentUserAccessor.UserName, dateTimeProvider.UtcNow),
            _ => throw new DomainRuleException("El medio de reembolso no es válido.")
        });

        await repository.AddFinancialAuditAsync(new SistemiPos.Domain.Audit.FinancialAuditEntry
        {
            OccurredAtUtc = dateTimeProvider.UtcNow, UserName = currentUserAccessor.UserName, Action = "Reembolso de venta",
            CashSessionId = cashSession.Id, DocumentType = nameof(Sale), DocumentId = sale.Id, DocumentReference = sale.Number,
            Amount = request.Amount, AuthorizedByUserName = refundAuthorizedBy, Comment = request.Reason
        }, cancellationToken);

        await repository.AddRefundAsync(refund, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task VerifyTransferAsync(Guid saleId, string? observation, CancellationToken cancellationToken = default)
    {
        if (!await repository.UserHasPermissionAsync(currentUserAccessor.UserName, PermissionCodes.TransfersVerify, cancellationToken))
        {
            throw new DomainRuleException("El usuario no tiene permiso para verificar transferencias.");
        }

        var sale = await repository.GetSaleAsync(saleId, cancellationToken)
            ?? throw new NotFoundException("Venta no encontrada.");

        sale.Payment.VerifyTransfer(currentUserAccessor.UserName, dateTimeProvider.UtcNow, observation);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectTransferAsync(Guid saleId, string? observation, CancellationToken cancellationToken = default)
    {
        if (!await repository.UserHasPermissionAsync(currentUserAccessor.UserName, PermissionCodes.TransfersVerify, cancellationToken))
        {
            throw new DomainRuleException("El usuario no tiene permiso para rechazar transferencias.");
        }

        var sale = await repository.GetSaleAsync(saleId, cancellationToken)
            ?? throw new NotFoundException("Venta no encontrada.");

        sale.Payment.RejectTransfer(currentUserAccessor.UserName, dateTimeProvider.UtcNow, observation);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<SalePayment> BuildPaymentAsync(SalePaymentRequest request, decimal total, SystemSetting settings, CancellationToken cancellationToken)
    {
        return request.PaymentMethod switch
        {
            PaymentMethodType.Cash when !settings.CashPaymentsEnabled => throw new DomainRuleException("La forma de pago en efectivo está deshabilitada."),
            PaymentMethodType.Cash => SalePayment.CreateCash(
                total,
                request.ReceivedAmount ?? 0m,
                currentUserAccessor.UserName),
            PaymentMethodType.Transfer when !settings.TransferPaymentsEnabled => throw new DomainRuleException("La forma de pago por transferencia está deshabilitada."),
            PaymentMethodType.Transfer => await BuildTransferPaymentAsync(request, total, cancellationToken),
            PaymentMethodType.Pos when !settings.PosPaymentsEnabled => throw new DomainRuleException("La forma de pago POS está deshabilitada."),
            PaymentMethodType.Pos => SalePayment.CreatePos(
                total,
                request.CardType ?? throw new DomainRuleException("El tipo de tarjeta es requerido."),
                request.CardLast4 ?? string.Empty,
                request.CardAuthorizationNumber ?? string.Empty,
                request.CardVoucherReference ?? string.Empty,
                request.CardAmount ?? total,
                request.CardStatus ?? CardPaymentStatus.Pending,
                currentUserAccessor.UserName,
                (await repository.GetPosDeviceAsync(request.PosDeviceId ?? Guid.Empty, cancellationToken))?.Id
                    ?? throw new DomainRuleException("Seleccione un dispositivo POS activo.")),
            PaymentMethodType.Credit when !settings.CreditPaymentsEnabled => throw new DomainRuleException("La forma de pago a crédito está deshabilitada."),
            PaymentMethodType.Credit => SalePayment.CreateCredit(total, currentUserAccessor.UserName),
            _ => throw new DomainRuleException("La forma de pago no es válida.")
        };
    }

    private async Task<string?> ResolveSensitiveAuthorizationAsync(string withoutAuthorizationPermission, string authorizePermission,
        string? authorizerUserName, string? authorizerPassword, CancellationToken cancellationToken)
    {
        if (operationAuthorizationService is null) return authorizerUserName;
        if (await operationAuthorizationService.HasPermissionAsync(currentUserAccessor.UserName, withoutAuthorizationPermission, cancellationToken)) return null;
        return await operationAuthorizationService.ValidateCredentialsAsync(authorizerUserName ?? string.Empty, authorizerPassword ?? string.Empty,
            authorizePermission, cancellationToken);
    }

    private async Task<Customer?> ResolveCustomerAsync(CreateSaleRequest request, CancellationToken cancellationToken)
    {
        if (request.CustomerId.HasValue)
        {
            var selectedCustomer = await repository.GetCustomerAsync(request.CustomerId.Value, cancellationToken)
                ?? throw new NotFoundException("Cliente no encontrado.");

            if (!selectedCustomer.IsActive)
            {
                throw new DomainRuleException("No se puede vender a un cliente inactivo.");
            }

            selectedCustomer.CompleteMissingSaleContact(request.CustomerPhone, request.CustomerVisitOrigin);
            return selectedCustomer;
        }

        var normalizedNit = NitNormalizer.Normalize(request.CustomerNit);
        var isFinalConsumer = normalizedNit is null or "CF";
        if (isFinalConsumer)
        {
            var hasPhone = !string.IsNullOrWhiteSpace(request.CustomerPhone);
            var hasVisitOrigin = !string.IsNullOrWhiteSpace(request.CustomerVisitOrigin);
            if (!hasPhone && !hasVisitOrigin) return null;

            if (hasPhone)
            {
                var existingByPhone = await repository.GetActiveCustomerByPhoneAsync(request.CustomerPhone!, cancellationToken);
                if (existingByPhone is not null)
                {
                    existingByPhone.CompleteMissingSaleContact(request.CustomerPhone, request.CustomerVisitOrigin);
                    return existingByPhone;
                }
            }

            var finalConsumer = new Customer(
                string.IsNullOrWhiteSpace(request.CustomerName) ? "Consumidor final" : request.CustomerName,
                null,
                request.CustomerPhone,
                null,
                request.CustomerAddress);
            finalConsumer.CompleteMissingSaleContact(request.CustomerPhone, request.CustomerVisitOrigin);
            await repository.AddCustomerAsync(finalConsumer, cancellationToken);
            return finalConsumer;
        }

        if (normalizedNit is null)
        {
            if (!string.IsNullOrWhiteSpace(request.CustomerName))
            {
                throw new DomainRuleException("El NIT es requerido para guardar el cliente de la venta.");
            }

            return null;
        }

        var existingCustomer = await repository.GetActiveCustomerByNormalizedNitAsync(normalizedNit, cancellationToken);
        if (existingCustomer is not null)
        {
            existingCustomer.UpdateSaleContact(request.CustomerName, request.CustomerAddress);
            existingCustomer.CompleteMissingSaleContact(request.CustomerPhone, request.CustomerVisitOrigin);
            return existingCustomer;
        }

        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            throw new DomainRuleException("La razón social es requerida cuando el NIT no existe en clientes.");
        }

        var newCustomer = new Customer(request.CustomerName, request.CustomerNit, request.CustomerPhone, null, request.CustomerAddress);
        newCustomer.CompleteMissingSaleContact(request.CustomerPhone, request.CustomerVisitOrigin);
        await repository.AddCustomerAsync(newCustomer, cancellationToken);
        return newCustomer;
    }

    private static string? FirstNotBlank(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred.Trim() :
        !string.IsNullOrWhiteSpace(fallback) ? fallback.Trim() : null;

    private static string NormalizeCustomerNitForSnapshot(string? nit) =>
        NitNormalizer.Normalize(nit) is null or "CF" ? "C/F" : nit!.Trim();

    private async Task<SalePayment> BuildTransferPaymentAsync(SalePaymentRequest request, decimal total, CancellationToken cancellationToken)
    {
        var bankAccountId = request.TransferBankAccountId
            ?? throw new DomainRuleException("La cuenta destino es requerida.");
        var bankAccount = await repository.GetBankAccountAsync(bankAccountId, cancellationToken)
            ?? throw new NotFoundException("Cuenta bancaria no encontrada.");

        if (!bankAccount.IsActive)
        {
            throw new DomainRuleException("La cuenta bancaria seleccionada está inactiva.");
        }

        return SalePayment.CreateTransfer(
            total,
            request.TransferSourceBank ?? string.Empty,
            bankAccount.Id,
            bankAccount.Bank,
            bankAccount.AccountName,
            bankAccount.AccountNumber,
            request.TransferReference ?? string.Empty,
            total,
            dateTimeProvider.UtcNow,
            request.TransferStatus ?? TransferPaymentStatus.Received,
            currentUserAccessor.UserName,
            request.TransferSenderName ?? string.Empty);
    }

    private static CashMovement? CreateCashMovement(Sale sale) =>
        sale.Payment.PaymentMethod switch
        {
            PaymentMethodType.Cash => new CashMovement(CashMovementType.SaleCash, sale.Total, $"Venta {sale.Number}", sale.CashierUserName, sale.SoldAtUtc),
            PaymentMethodType.Transfer => new CashMovement(CashMovementType.SaleTransfer, sale.Total, $"Venta {sale.Number}", sale.CashierUserName, sale.SoldAtUtc),
            PaymentMethodType.Pos => new CashMovement(CashMovementType.SalePos, sale.Total, $"Venta {sale.Number}", sale.CashierUserName, sale.SoldAtUtc),
            PaymentMethodType.Credit => null,
            _ => throw new DomainRuleException("La forma de pago no es válida.")
        };

    private static SaleDto Map(Sale sale) =>
        new(
            sale.Id,
            sale.Number,
            sale.CashSessionId,
            sale.CustomerId,
            sale.CustomerNameSnapshot ?? (sale.CustomerId.HasValue ? sale.Customer?.Name ?? "Cliente" : "Consumidor final"),
            sale.SoldAtUtc,
            sale.CashierUserName,
            sale.Subtotal,
            sale.DiscountTotal,
            sale.TaxableBaseTotal,
            sale.TaxTotal,
            sale.Total,
            sale.DiscountAppliedByUserName,
            sale.DiscountAppliedAtUtc,
            sale.DiscountReason,
            sale.DiscountAuthorizedByUserName,
            sale.DiscountAuthorizedAtUtc,
            sale.CurrencyCode,
            sale.CurrencyName,
            sale.CurrencySymbol,
            sale.CurrencyDecimals,
            sale.DecimalSeparator,
            sale.ThousandsSeparator,
            sale.CurrencySymbolPosition,
            sale.Status,
            new SalePaymentDto(
                sale.Payment.PaymentMethod,
                sale.Payment.TotalAmount,
                sale.Payment.ReceivedAmount,
                sale.Payment.ChangeAmount,
                sale.Payment.TransferSourceBank,
                sale.Payment.TransferDestinationBank,
                sale.Payment.TransferDestinationAccountName,
                sale.Payment.TransferDestinationAccountNumber,
                sale.Payment.TransferReference,
                sale.Payment.TransferAmount,
                sale.Payment.TransferDateUtc,
                sale.Payment.TransferStatus,
                sale.Payment.TransferReviewedByUserName,
                sale.Payment.TransferReviewedAtUtc,
                sale.Payment.TransferReviewObservation,
                sale.Payment.CardType,
                sale.Payment.CardLast4,
                sale.Payment.CardAuthorizationNumber,
                sale.Payment.CardVoucherReference,
                sale.Payment.CardAmount,
                sale.Payment.CardStatus,
                sale.Payment.TransferSenderName),
            sale.Items.Select(x => new SaleItemDto(
                x.ProductId,
                x.ProductName,
                x.Quantity,
                x.UnitPrice,
                x.DiscountPercent,
                x.DiscountAmount,
                x.TaxableBaseAmount,
                x.TaxPercent,
                x.TaxAmount,
                x.Subtotal,
                x.IsTaxExempt,
                x.GrossAmount)).ToList(),
            sale.CustomerNitSnapshot ?? sale.Customer?.Nit,
            sale.ClientOperationId,
            sale.SynchronizedAtUtc);
}
