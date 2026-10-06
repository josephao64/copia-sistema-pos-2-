using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SistemiPos.Application.Abstractions;
using SistemiPos.Domain.Audit;
using SistemiPos.Domain.Cash;
using SistemiPos.Domain.Catalog;
using SistemiPos.Domain.Configuration;
using SistemiPos.Domain.Customers;
using SistemiPos.Domain.Inventory;
using SistemiPos.Domain.Finance;
using SistemiPos.Domain.Organizations;
using SistemiPos.Domain.Purchases;
using SistemiPos.Domain.Sales;
using SistemiPos.Domain.Security;
using SistemiPos.Domain.Suppliers;
using SistemiPos.Infrastructure.Security;

namespace SistemiPos.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IUnitOfWork
{
    public DbSet<BusinessGroup> BusinessGroups => Set<BusinessGroup>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<UserScopeAssignment> UserScopeAssignments => Set<UserScopeAssignment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<FinancialAuditEntry> FinancialAuditEntries => Set<FinancialAuditEntry>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<OriginBank> OriginBanks => Set<OriginBank>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashCount> CashCounts => Set<CashCount>();
    public DbSet<CashCountLine> CashCountLines => Set<CashCountLine>();
    public DbSet<CashPosClosing> CashPosClosings => Set<CashPosClosing>();
    public DbSet<CashSessionPosDevice> CashSessionPosDevices => Set<CashSessionPosDevice>();
    public DbSet<CashDenomination> CashDenominations => Set<CashDenomination>();
    public DbSet<PosDevice> PosDevices => Set<PosDevice>();
    public DbSet<PaymentMethodDefinition> PaymentMethodDefinitions => Set<PaymentMethodDefinition>();
    public DbSet<CashMovementConcept> CashMovementConcepts => Set<CashMovementConcept>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductPriceRule> ProductPriceRules => Set<ProductPriceRule>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<ProductUnitConversion> ProductUnitConversions => Set<ProductUnitConversion>();
    public DbSet<ProductPurchasePresentation> ProductPurchasePresentations => Set<ProductPurchasePresentation>();
    public DbSet<ProductBranchSetting> ProductBranchSettings => Set<ProductBranchSetting>();
    public DbSet<CatalogChangeLog> CatalogChangeLogs => Set<CatalogChangeLog>();
    public DbSet<VariantType> VariantTypes => Set<VariantType>();
    public DbSet<VariantOption> VariantOptions => Set<VariantOption>();
    public DbSet<ProductVariantOption> ProductVariantOptions => Set<ProductVariantOption>();
    public DbSet<RoleDiscountSetting> RoleDiscountSettings => Set<RoleDiscountSetting>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleCancellation> SaleCancellations => Set<SaleCancellation>();
    public DbSet<SaleRefund> SaleRefunds => Set<SaleRefund>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<DiscountApprovalRequest> DiscountApprovalRequests => Set<DiscountApprovalRequest>();
    public DbSet<DiscountApprovalItem> DiscountApprovalItems => Set<DiscountApprovalItem>();
    public DbSet<FelCertification> FelCertifications => Set<FelCertification>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<CurrencyDefinition> Currencies => Set<CurrencyDefinition>();
    public DbSet<UserDiscountSetting> UserDiscountSettings => Set<UserDiscountSetting>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<InventoryStock> InventoryStocks => Set<InventoryStock>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<InventoryExpiryLot> InventoryExpiryLots => Set<InventoryExpiryLot>();
    public DbSet<PhysicalInventoryCount> PhysicalInventoryCounts => Set<PhysicalInventoryCount>();
    public DbSet<PhysicalInventoryCountCategory> PhysicalInventoryCountCategories => Set<PhysicalInventoryCountCategory>();
    public DbSet<PhysicalInventoryCountItem> PhysicalInventoryCountItems => Set<PhysicalInventoryCountItem>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptItem> PurchaseReceiptItems => Set<PurchaseReceiptItem>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<ProductSupplier> ProductSuppliers => Set<ProductSupplier>();
    public DbSet<SupplierProductPriceHistory> SupplierProductPriceHistory => Set<SupplierProductPriceHistory>();
    public DbSet<PurchasePresentation> PurchasePresentations => Set<PurchasePresentation>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<PayableAccount> PayableAccounts => Set<PayableAccount>();
    public DbSet<PayablePayment> PayablePayments => Set<PayablePayment>();
    public DbSet<ReceivableAccount> ReceivableAccounts => Set<ReceivableAccount>();
    public DbSet<ReceivablePayment> ReceivablePayments => Set<ReceivablePayment>();
    public DbSet<PettyCashFund> PettyCashFunds => Set<PettyCashFund>();
    public DbSet<PettyCashMovement> PettyCashMovements => Set<PettyCashMovement>();
    public DbSet<PettyCashSettlement> PettyCashSettlements => Set<PettyCashSettlement>();
    public DbSet<PettyCashCycle> PettyCashCycles => Set<PettyCashCycle>();
    public DbSet<PettyCashCategory> PettyCashCategories => Set<PettyCashCategory>();

    public DbSet<InventoryMovementLot> InventoryMovementLots => Set<InventoryMovementLot>();
    public DbSet<CompanySetting> CompanySettings => Set<CompanySetting>();
    public DbSet<BranchSetting> BranchSettings => Set<BranchSetting>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SistemiPos.Infrastructure.Inventory.InventoryLotLedger.PrepareAsync(this, CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await SistemiPos.Infrastructure.Inventory.InventoryLotLedger.PrepareAsync(this, cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasSequence<long>("ProductInternalCodeSequence");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
