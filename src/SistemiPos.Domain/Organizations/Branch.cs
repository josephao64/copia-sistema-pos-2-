using SistemiPos.Domain.Common;
using SistemiPos.Domain.Common.Exceptions;

namespace SistemiPos.Domain.Organizations;

public sealed class Branch : AuditableEntity
{
    public Guid CompanyId { get; private set; }
    public Company Company { get; set; } = null!;

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string TimeZone { get; private set; } = "America/Guatemala";
    public string? EstablishmentCode { get; private set; }
    public bool IsActive { get; private set; } = true;
    public BranchSetting? Setting { get; set; }

    private readonly List<Warehouse> _warehouses = [];
    public IReadOnlyCollection<Warehouse> Warehouses => _warehouses.AsReadOnly();

    private Branch()
    {
    }

    public Branch(
        Guid companyId,
        string code,
        string name,
        string? address = null,
        string? phone = null,
        string? timeZone = "America/Guatemala",
        string? establishmentCode = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new DomainRuleException("La empresa es requerida para la sucursal.");
        }

        CompanyId = companyId;
        Update(code, name, address, phone, timeZone, establishmentCode);
    }

    public void Update(
        string code,
        string name,
        string? address = null,
        string? phone = null,
        string? timeZone = "America/Guatemala",
        string? establishmentCode = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainRuleException("El código de la sucursal es requerido.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("El nombre de la sucursal es requerido.");
        }

        Code = code.Trim().ToUpperInvariant();
        Name = NameNormalizer.Normalize(name);
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? "America/Guatemala" : timeZone.Trim();
        EstablishmentCode = string.IsNullOrWhiteSpace(establishmentCode) ? null : establishmentCode.Trim();
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
