using SistemiPos.Domain.Catalog;
using SistemiPos.Domain.Common;
using SistemiPos.Domain.Common.Exceptions;

namespace SistemiPos.Domain.Purchases;

public sealed class PurchaseOrderItem : Entity
{
    public Guid PurchaseOrderId { get; set; }
    public Guid ProductId { get; private set; }
    public Product Product { get; set; } = null!;
    public Guid? PresentationId { get; private set; }
    public PurchasePresentation? Presentation { get; set; }
    public string PresentationName { get; private set; } = "Unidad";
    public decimal PackageQuantity { get; private set; }
    public int UnitsPerPackage { get; private set; } = 1;
    public decimal RequestedQuantity { get; private set; }

    private PurchaseOrderItem()
    {
    }

    public PurchaseOrderItem(Guid productId, decimal requestedQuantity)
        : this(productId, requestedQuantity, null, "Unidad", 1)
    {
    }

    public PurchaseOrderItem(Guid productId, decimal packageQuantity, Guid? presentationId,
        string presentationName, int unitsPerPackage)
    {
        if (productId == Guid.Empty || packageQuantity <= 0)
        {
            throw new DomainRuleException("El producto y la cantidad solicitada son requeridos.");
        }
        if (unitsPerPackage <= 0 || string.IsNullOrWhiteSpace(presentationName))
        {
            throw new DomainRuleException("La presentación seleccionada no es válida.");
        }

        ProductId = productId;
        SetPresentation(packageQuantity, presentationId, presentationName, unitsPerPackage);
    }

    public void UpdatePresentation(decimal packageQuantity, Guid? presentationId,
        string presentationName, int unitsPerPackage)
    {
        if (packageQuantity <= 0)
        {
            throw new DomainRuleException("La cantidad solicitada debe ser mayor a cero.");
        }
        if (unitsPerPackage <= 0 || string.IsNullOrWhiteSpace(presentationName))
        {
            throw new DomainRuleException("La presentación seleccionada no es válida.");
        }

        SetPresentation(packageQuantity, presentationId, presentationName, unitsPerPackage);
    }

    private void SetPresentation(decimal packageQuantity, Guid? presentationId,
        string presentationName, int unitsPerPackage)
    {
        PresentationId = presentationId;
        PresentationName = presentationName.Trim();
        PackageQuantity = decimal.Round(packageQuantity, 2);
        UnitsPerPackage = unitsPerPackage;
        RequestedQuantity = decimal.Round(PackageQuantity * UnitsPerPackage, 2);
    }
}
