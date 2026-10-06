using SistemiPos.Domain.Common;
using SistemiPos.Domain.Common.Exceptions;

namespace SistemiPos.Domain.Sales;

public sealed class SaleItem : Entity
{
    public Guid SaleId { get; set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal CostAtSale { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxableBaseAmount { get; private set; }
    public decimal TaxPercent { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal Subtotal { get; private set; }
    public bool IsTaxExempt { get; private set; }

    private SaleItem()
    {
    }

    public SaleItem(
        Guid productId,
        string productName,
        decimal quantity,
        decimal unitPrice,
        decimal discountPercent,
        decimal taxPercent,
        bool isTaxExempt,
        decimal costAtSale = 0m,
        decimal? grossAmount = null,
        decimal? fixedDiscountAmount = null)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainRuleException("El producto es requerido.");
        }

        if (string.IsNullOrWhiteSpace(productName))
        {
            throw new DomainRuleException("El nombre del producto es requerido.");
        }

        if (quantity <= 0 || unitPrice < 0 || discountPercent < 0 || taxPercent < 0 || costAtSale < 0)
        {
            throw new DomainRuleException("Los valores de la venta no son válidos.");
        }

        if (grossAmount is < 0 || fixedDiscountAmount is < 0)
        {
            throw new DomainRuleException("Los importes de la línea no pueden ser negativos.");
        }

        var gross = grossAmount.HasValue
            ? decimal.Round(grossAmount.Value, 2)
            : decimal.Round(quantity * unitPrice, 2);
        var discountAmount = fixedDiscountAmount.HasValue
            ? decimal.Round(fixedDiscountAmount.Value, 2)
            : decimal.Round(gross * (discountPercent / 100m), 2);
        if (discountAmount > gross)
        {
            throw new DomainRuleException("El descuento no puede superar el total de la línea.");
        }

        var net = decimal.Round(gross - discountAmount, 2);
        var effectiveTaxPercent = isTaxExempt ? 0m : taxPercent;
        var taxableBase = effectiveTaxPercent == 0m
            ? net
            : decimal.Round(net / (1m + (effectiveTaxPercent / 100m)), 2);
        var taxAmount = decimal.Round(net - taxableBase, 2);

        ProductId = productId;
        ProductName = productName.Trim();
        Quantity = decimal.Round(quantity, 2);
        UnitPrice = decimal.Round(unitPrice, 6);
        GrossAmount = gross;
        CostAtSale = decimal.Round(costAtSale, 2);
        DiscountPercent = fixedDiscountAmount.HasValue && gross > 0
            ? decimal.Round(discountAmount / gross * 100m, 2)
            : decimal.Round(discountPercent, 2);
        DiscountAmount = discountAmount;
        TaxableBaseAmount = taxableBase;
        TaxPercent = decimal.Round(effectiveTaxPercent, 2);
        TaxAmount = taxAmount;
        Subtotal = net;
        IsTaxExempt = isTaxExempt;
    }
}
