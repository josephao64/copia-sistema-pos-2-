using SistemiPos.Domain.Sales;

namespace SistemiPos.Web.Models.Sales;

public sealed class RefundSaleViewModel
{
    public Guid SaleId { get; set; }
    public Guid CashSessionId { get; set; }
    public decimal Amount { get; set; }
    public RefundMethodType RefundMethod { get; set; } = RefundMethodType.Cash;
    public string Reason { get; set; } = string.Empty;
    public string AuthorizedByUserName { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? AuthorizerPassword { get; set; }
}
