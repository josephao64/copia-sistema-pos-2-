using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemiPos.Application.Cash;
using SistemiPos.Application.Catalog;
using SistemiPos.Application.Customers;
using SistemiPos.Application.Sales;
using SistemiPos.Application.Security;
using SistemiPos.Application.Settings;
using SistemiPos.Domain.Cash;
using SistemiPos.Domain.Common.Exceptions;
using SistemiPos.Domain.Sales;
using SistemiPos.Web.Models.Offline;

namespace SistemiPos.Web.Controllers.Api;

[ApiController]
[Route("api/offline-pos")]
[Authorize(Policy = RoleAccessPolicies.SalesAccess)]
[Authorize(Policy = PermissionCodes.SalesManage)]
public sealed class OfflinePosController(
    ICatalogService catalogService,
    ICustomerService customerService,
    ICashService cashService,
    ISettingsService settingsService,
    ISaleService saleService) : ControllerBase
{
    [HttpGet("bootstrap")]
    public async Task<ActionResult<OfflineBootstrapResponse>> GetBootstrapAsync(CancellationToken cancellationToken)
    {
        var products = await catalogService.SearchProductsAsync(null, cancellationToken);
        var customers = await customerService.GetCustomersAsync(cancellationToken);
        var currentSession = await cashService.GetCurrentSessionAsync(cancellationToken);
        var settings = await settingsService.GetSystemSettingAsync(cancellationToken);
        var bankAccounts = await settingsService.GetBankAccountsAsync(cancellationToken);

        return Ok(new OfflineBootstrapResponse(
            products.Where(x => x.IsActive && !x.ManagesVariants).Select(x => new OfflineProductDto(
                x.Id,
                x.InternalCode,
                x.Barcode,
                x.DisplayName ?? x.Name,
                x.SalePrice,
                x.CurrentStock,
                x.MultiPriceEnabled,
                x.MultiPriceMode.ToString(),
                (x.PriceRules ?? []).Select(rule => new OfflineProductPriceRuleDto(rule.Quantity, rule.Price, rule.IsActive)).ToList())).ToList(),
            customers.Where(x => x.IsActive).Select(x => new OfflineCustomerDto(x.Id, x.Name, x.Nit)).ToList(),
            currentSession is null ? [] : [new OfflineCashSessionDto(currentSession.Id, currentSession.RegisterCode, currentSession.CashierUserName)],
            bankAccounts.Where(x => x.IsActive).Select(x => new OfflineBankAccountDto(x.Id, x.Bank, x.AccountName, x.AccountNumber)).ToList(),
            new OfflineSettingDto(
                settings.BusinessName,
                settings.BusinessNit,
                settings.BusinessAddress,
                settings.BusinessPhone,
                settings.CurrencyCode,
                settings.CurrencyName,
                settings.CurrencySymbol,
                settings.CurrencyDecimals,
                settings.DecimalSeparator,
                settings.ThousandsSeparator,
                settings.CurrencySymbolPosition.ToString(),
                settings.TaxPercent,
                settings.ShowTaxOnReceipt,
                settings.ReceiptFooter,
                settings.CashPaymentsEnabled,
                settings.TransferPaymentsEnabled,
                settings.PosPaymentsEnabled,
                DateTimeOffset.UtcNow)));
    }

    [HttpPost("sync")]
    public async Task<ActionResult<OfflineSyncBatchResponse>> SyncAsync([FromBody] OfflineSyncBatchRequest request, CancellationToken cancellationToken)
    {
        var results = new List<OfflineSyncResultDto>();

        foreach (var sale in request.Sales)
        {
            try
            {
                if (sale.Items.Any(x => x.DiscountPercent > 0))
                {
                    results.Add(new OfflineSyncResultDto(
                        sale.ClientOperationId,
                        "Conflict",
                        null,
                        "Las ventas con descuento requieren conexión y aprobación de un superior antes de guardarse."));
                    continue;
                }

                if (sale.PaymentMethod is not (PaymentMethodType.Cash or PaymentMethodType.Transfer or PaymentMethodType.Pos))
                {
                    results.Add(new OfflineSyncResultDto(
                        sale.ClientOperationId,
                        "Conflict",
                        null,
                        "La forma de pago seleccionada requiere conexión con el servidor."));
                    continue;
                }

                var saleId = await saleService.CreateSaleAsync(new CreateSaleRequest(
                    sale.CashSessionId,
                    sale.CustomerId,
                    sale.Items.Select(x => new SaleItemRequest(x.ProductId, x.Quantity, x.DiscountPercent)).ToList(),
                    new SalePaymentRequest(
                        sale.PaymentMethod,
                        sale.ReceivedAmount,
                        sale.TransferSourceBank,
                        sale.TransferBankAccountId,
                        sale.TransferReference,
                        sale.TransferAmount,
                        sale.TransferDateUtc,
                        sale.TransferStatus,
                        sale.CardType,
                        sale.CardLast4,
                        sale.CardAuthorizationNumber,
                        sale.CardVoucherReference,
                        sale.CardAmount,
                        sale.CardStatus,
                        sale.PosDeviceId,
                        sale.TransferSenderName),
                    ClientOperationId: sale.ClientOperationId,
                    LocalCreatedAtUtc: sale.LocalCreatedAtUtc,
                    CustomerNit: sale.CustomerNit,
                    CustomerName: sale.CustomerName,
                    CustomerAddress: sale.CustomerAddress,
                    CustomerPhone: sale.CustomerPhone,
                    CustomerVisitOrigin: sale.CustomerVisitOrigin), cancellationToken);

                var syncedSale = await saleService.GetSaleAsync(saleId, cancellationToken);
                results.Add(new OfflineSyncResultDto(sale.ClientOperationId, "Synced", syncedSale?.Number, null));
            }
            catch (DomainRuleException ex)
            {
                results.Add(new OfflineSyncResultDto(sale.ClientOperationId, "Conflict", null, ex.Message));
            }
            catch (Exception ex)
            {
                results.Add(new OfflineSyncResultDto(sale.ClientOperationId, "Error", null, ex.Message));
            }
        }

        return Ok(new OfflineSyncBatchResponse(results));
    }
}
