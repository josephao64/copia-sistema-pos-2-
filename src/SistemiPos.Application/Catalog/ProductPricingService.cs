using SistemiPos.Domain.Catalog;
using SistemiPos.Domain.Common.Exceptions;

namespace SistemiPos.Application.Catalog;

public sealed class ProductPricingService : IProductPricingService
{
    public static ProductPricingService Instance { get; } = new();

    public ProductPriceQuote Calculate(Product product, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (quantity <= 0)
        {
            throw new DomainRuleException("La cantidad vendida debe ser mayor que cero.");
        }

        var normalizedQuantity = decimal.Round(quantity, 2);
        if (normalizedQuantity <= 0)
        {
            throw new DomainRuleException("La cantidad vendida debe ser mayor que cero.");
        }
        if (!product.MultiPriceEnabled)
        {
            return BasePrice(product, normalizedQuantity);
        }

        var activeRules = product.PriceRules.Where(x => x.IsActive);
        var rule = product.MultiPriceMode switch
        {
            MultiPriceMode.MinimumQuantity => activeRules
                .Where(x => x.Quantity <= normalizedQuantity)
                .OrderByDescending(x => x.Quantity)
                .FirstOrDefault(),
            MultiPriceMode.ExactQuantity => activeRules
                .FirstOrDefault(x => x.Quantity == normalizedQuantity),
            _ => throw new DomainRuleException("El tipo de multiprecio no es válido.")
        };

        if (rule is null)
        {
            return BasePrice(product, normalizedQuantity);
        }

        if (product.MultiPriceMode == MultiPriceMode.ExactQuantity)
        {
            return new ProductPriceQuote(
                decimal.Round(rule.Price / normalizedQuantity, 6),
                rule.Price,
                rule.Id,
                product.MultiPriceMode);
        }

        return new ProductPriceQuote(
            rule.Price,
            decimal.Round(rule.Price * normalizedQuantity, 2),
            rule.Id,
            product.MultiPriceMode);
    }

    private static ProductPriceQuote BasePrice(Product product, decimal quantity) =>
        new(product.SalePrice, decimal.Round(product.SalePrice * quantity, 2), null, null);
}
