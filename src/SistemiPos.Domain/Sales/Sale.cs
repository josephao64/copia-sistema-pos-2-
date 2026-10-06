using SistemiPos.Domain.Common;
using SistemiPos.Domain.Common.Exceptions;
using SistemiPos.Domain.Customers;

namespace SistemiPos.Domain.Sales;

public sealed class Sale : AuditableEntity
{
    private readonly List<SaleItem> _items = [];

    public string Number { get; private set; } = string.Empty;
    public string? ClientOperationId { get; private set; }
    public Guid CashSessionId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Customer? Customer { get; set; }
    public string? CustomerNitSnapshot { get; private set; }
    public string? CustomerNameSnapshot { get; private set; }
    public string? CustomerAddressSnapshot { get; private set; }
    public string? CustomerPhoneSnapshot { get; private set; }
    public string? CustomerVisitOriginSnapshot { get; private set; }
    public DateTimeOffset SoldAtUtc { get; private set; }
    public DateTimeOffset? SynchronizedAtUtc { get; private set; }
    public string CashierUserName { get; private set; } = string.Empty;
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxableBaseTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    public string? DiscountAppliedByUserName { get; private set; }
    public DateTimeOffset? DiscountAppliedAtUtc { get; private set; }
    public string? DiscountReason { get; private set; }
    public string? DiscountAuthorizedByUserName { get; private set; }
    public DateTimeOffset? DiscountAuthorizedAtUtc { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public string CurrencyName { get; private set; } = string.Empty;
    public string CurrencySymbol { get; private set; } = string.Empty;
    public int CurrencyDecimals { get; private set; }
    public string DecimalSeparator { get; private set; } = string.Empty;
    public string ThousandsSeparator { get; private set; } = string.Empty;
    public string CurrencySymbolPosition { get; private set; } = string.Empty;
    public SaleStatus Status { get; private set; } = SaleStatus.Confirmed;
    public IReadOnlyCollection<SaleItem> Items => _items;
    public SalePayment Payment { get; private set; } = null!;

    private Sale()
    {
    }

    public Sale(
        string number,
        Guid cashSessionId,
        Guid? customerId,
        DateTimeOffset soldAtUtc,
        string cashierUserName,
        IEnumerable<SaleItem> items,
        SalePayment payment,
        string currencyCode,
        string currencyName,
        string currencySymbol,
        int currencyDecimals,
        string decimalSeparator,
        string thousandsSeparator,
        string currencySymbolPosition,
        string? customerNitSnapshot = null,
        string? customerNameSnapshot = null,
        string? customerAddressSnapshot = null,
        string? customerPhoneSnapshot = null,
        string? customerVisitOriginSnapshot = null,
        string? clientOperationId = null)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new DomainRuleException("El número de venta es requerido.");
        }

        if (cashSessionId == Guid.Empty)
        {
            throw new DomainRuleException("La caja activa es requerida.");
        }

        Number = number.Trim();
        CashSessionId = cashSessionId;
        CustomerId = customerId;
        SoldAtUtc = soldAtUtc;
        CashierUserName = string.IsNullOrWhiteSpace(cashierUserName) ? "system" : cashierUserName.Trim();
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        CurrencyName = currencyName.Trim();
        CurrencySymbol = currencySymbol.Trim();
        CurrencyDecimals = currencyDecimals;
        DecimalSeparator = decimalSeparator;
        ThousandsSeparator = thousandsSeparator;
        CurrencySymbolPosition = currencySymbolPosition.Trim();
        CustomerNitSnapshot = string.IsNullOrWhiteSpace(customerNitSnapshot) ? null : customerNitSnapshot.Trim();
        CustomerNameSnapshot = string.IsNullOrWhiteSpace(customerNameSnapshot) ? null : customerNameSnapshot.Trim();
        CustomerAddressSnapshot = string.IsNullOrWhiteSpace(customerAddressSnapshot) ? null : customerAddressSnapshot.Trim();
        CustomerPhoneSnapshot = string.IsNullOrWhiteSpace(customerPhoneSnapshot) ? null : customerPhoneSnapshot.Trim();
        CustomerVisitOriginSnapshot = string.IsNullOrWhiteSpace(customerVisitOriginSnapshot) ? null : customerVisitOriginSnapshot.Trim();
        ClientOperationId = string.IsNullOrWhiteSpace(clientOperationId) ? null : clientOperationId.Trim();

        ReplaceItems(items);
        SetPayment(payment);
    }

    public void ReplaceItems(IEnumerable<SaleItem> items)
    {
        var values = items.ToList();
        if (values.Count == 0)
        {
            throw new DomainRuleException("La venta debe incluir al menos un producto.");
        }

        _items.Clear();
        _items.AddRange(values);

        Subtotal = decimal.Round(_items.Sum(x => x.Subtotal + x.DiscountAmount), 2);
        DiscountTotal = decimal.Round(_items.Sum(x => x.DiscountAmount), 2);
        TaxableBaseTotal = decimal.Round(_items.Sum(x => x.TaxableBaseAmount), 2);
        TaxTotal = decimal.Round(_items.Sum(x => x.TaxAmount), 2);
        Total = decimal.Round(_items.Sum(x => x.Subtotal), 2);
    }

    public void SetPayment(SalePayment payment)
    {
        Payment = payment ?? throw new DomainRuleException("El pago es requerido.");

        if (!Payment.AllowsSaleConfirmation())
        {
            throw new DomainRuleException("El estado del pago no permite confirmar la venta.");
        }

        if (decimal.Round(Payment.TotalAmount, 2) != Total)
        {
            throw new DomainRuleException("El total del pago debe coincidir con el total de la venta.");
        }
    }

    public void RegisterDiscountAudit(
        string appliedByUserName,
        DateTimeOffset appliedAtUtc,
        string? reason,
        string? authorizedByUserName,
        DateTimeOffset? authorizedAtUtc)
    {
        if (DiscountTotal <= 0)
        {
            DiscountAppliedByUserName = null;
            DiscountAppliedAtUtc = null;
            DiscountReason = null;
            DiscountAuthorizedByUserName = null;
            DiscountAuthorizedAtUtc = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(appliedByUserName))
        {
            throw new DomainRuleException("El usuario que aplica el descuento es requerido.");
        }

        DiscountAppliedByUserName = appliedByUserName.Trim();
        DiscountAppliedAtUtc = appliedAtUtc;
        DiscountReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DiscountAuthorizedByUserName = string.IsNullOrWhiteSpace(authorizedByUserName) ? null : authorizedByUserName.Trim();
        DiscountAuthorizedAtUtc = authorizedAtUtc;
    }

    public void Cancel()
    {
        if (Status == SaleStatus.Cancelled)
        {
            throw new DomainRuleException("La venta ya fue anulada.");
        }

        if (Status != SaleStatus.Confirmed)
        {
            throw new DomainRuleException("Solo se puede anular una venta confirmada.");
        }

        Status = SaleStatus.Cancelled;
    }

    public void MarkPartiallyReturned()
    {
        if (Status == SaleStatus.Cancelled)
        {
            throw new DomainRuleException("La venta anulada no admite devoluciones.");
        }

        if (Status == SaleStatus.Returned)
        {
            throw new DomainRuleException("La venta ya fue devuelta completamente.");
        }

        Status = SaleStatus.PartiallyReturned;
    }

    public void MarkReturned()
    {
        if (Status == SaleStatus.Cancelled)
        {
            throw new DomainRuleException("La venta anulada no admite devoluciones.");
        }

        if (Status == SaleStatus.Returned)
        {
            throw new DomainRuleException("La venta ya fue devuelta completamente.");
        }

        Status = SaleStatus.Returned;
    }

    public void MarkSynchronized(DateTimeOffset synchronizedAtUtc)
    {
        SynchronizedAtUtc = synchronizedAtUtc;
    }

}
