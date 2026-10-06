using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using SistemiPos.Application.Organizations;

namespace SistemiPos.Web.Models.Organizations;

public sealed class OrganizationIndexViewModel
{
    public string ActiveTab { get; set; } = "branches";
    public IReadOnlyList<BusinessGroupDto> BusinessGroups { get; set; } = [];
    public IReadOnlyList<CompanyDto> Companies { get; set; } = [];
    public IReadOnlyList<BranchDto> Branches { get; set; } = [];
    public IReadOnlyList<WarehouseDto> Warehouses { get; set; } = [];
}

public sealed class BusinessGroupFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "El código es requerido.")]
    [Display(Name = "Código")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre es requerido.")]
    [Display(Name = "Nombre del grupo")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Razón social")]
    public string? LegalName { get; set; }

    [Display(Name = "NIT / Identificación tributaria")]
    public string? TaxNumber { get; set; }

    [Display(Name = "Descripción")]
    public string? Description { get; set; }

    [Display(Name = "Activo")]
    public bool IsActive { get; set; } = true;
}

public sealed class CompanyFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "El grupo empresarial es requerido.")]
    [Display(Name = "Grupo empresarial")]
    public Guid BusinessGroupId { get; set; }

    [Required(ErrorMessage = "El código es requerido.")]
    [Display(Name = "Código")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre comercial es requerido.")]
    [Display(Name = "Nombre comercial")]
    public string TradeName { get; set; } = string.Empty;

    [Display(Name = "Razón social")]
    public string? LegalName { get; set; }

    [Display(Name = "NIT")]
    public string? Nit { get; set; }

    [Display(Name = "Correo electrónico")]
    [EmailAddress(ErrorMessage = "El correo no es válido.")]
    public string? Email { get; set; }

    [Display(Name = "Teléfono")]
    public string? Phone { get; set; }

    [Display(Name = "Dirección fiscal")]
    public string? Address { get; set; }

    [Display(Name = "Régimen fiscal")]
    public string? TaxRegime { get; set; }

    [Display(Name = "Activo")]
    public bool IsActive { get; set; } = true;

    public IReadOnlyList<SelectListItem> AvailableBusinessGroups { get; set; } = [];
}

public sealed class BranchFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "La empresa es requerida.")]
    [Display(Name = "Empresa fiscal")]
    public Guid CompanyId { get; set; }

    [Required(ErrorMessage = "El código es requerido.")]
    [Display(Name = "Código")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre de la sucursal es requerido.")]
    [Display(Name = "Nombre de la sucursal")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Dirección física")]
    public string? Address { get; set; }

    [Display(Name = "Teléfono")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "La zona horaria es requerida.")]
    [Display(Name = "Zona horaria")]
    public string TimeZone { get; set; } = "America/Guatemala";

    [Display(Name = "Código de establecimiento (SAT / FEL)")]
    public string? EstablishmentCode { get; set; }

    [Display(Name = "Activo")]
    public bool IsActive { get; set; } = true;

    public IReadOnlyList<SelectListItem> AvailableCompanies { get; set; } = [];
}

public sealed class WarehouseFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "La sucursal es requerida.")]
    [Display(Name = "Sucursal")]
    public Guid BranchId { get; set; }

    [Required(ErrorMessage = "El código es requerido.")]
    [Display(Name = "Código")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre de la bodega es requerido.")]
    [Display(Name = "Nombre de la bodega")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Descripción")]
    public string? Description { get; set; }

    [Display(Name = "Bodega principal predeterminada")]
    public bool IsDefault { get; set; }

    [Display(Name = "Punto de cocina / preparación")]
    public bool IsKitchen { get; set; }

    [Display(Name = "Activo")]
    public bool IsActive { get; set; } = true;

    public IReadOnlyList<SelectListItem> AvailableBranches { get; set; } = [];
}
