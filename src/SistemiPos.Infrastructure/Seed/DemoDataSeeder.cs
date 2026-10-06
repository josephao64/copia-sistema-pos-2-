using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SistemiPos.Application.Security;
using SistemiPos.Domain.Audit;
using SistemiPos.Domain.Cash;
using SistemiPos.Domain.Catalog;
using SistemiPos.Domain.Common;
using SistemiPos.Domain.Common.Exceptions;
using SistemiPos.Domain.Configuration;
using SistemiPos.Domain.Customers;
using SistemiPos.Domain.Finance;
using SistemiPos.Domain.Inventory;
using SistemiPos.Domain.Purchases;
using SistemiPos.Domain.Sales;
using SistemiPos.Domain.Suppliers;
using SistemiPos.Infrastructure.Persistence;
using SistemiPos.Infrastructure.Security;

namespace SistemiPos.Infrastructure.Seed;

public sealed class DemoDataSeeder(
    AppDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IConfiguration configuration)
{
    private const string Prefix = "DEMO-";
    private static readonly DateTimeOffset BaseDate = new(2026, 9, 2, 14, 0, 0, TimeSpan.Zero);

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("DemoData:Enabled"))
        {
            return;
        }

        if (await db.AuditLogs.AnyAsync(x => x.RecordType == "DemoData" && x.RecordId == "DEMO-SEED-COMPLETE", cancellationToken))
        {
            await EnsureMissingDemoReviewDataAsync(cancellationToken);
            return;
        }

        var admin = await EnsureUserAsync("demo.admin", "demo.admin@sistemi.local", "Demo Administrador", SecurityRoleNames.SuperAdministrator);
        var manager = await EnsureUserAsync("demo.gerente", "demo.gerente@sistemi.local", "Demo Gerente de Tienda", SecurityRoleNames.Manager);
        var cashier = await EnsureUserAsync("demo.cajero", "demo.cajero@sistemi.local", "Demo Cajero Principal", SecurityRoleNames.PointOfSale);
        var cashierTwo = await EnsureUserAsync("demo.cajero2", "demo.cajero2@sistemi.local", "Demo Cajero Turno Tarde", SecurityRoleNames.PointOfSale);
        var purchasing = await EnsureUserAsync("demo.compras", "demo.compras@sistemi.local", "Demo Encargado de Compras", SecurityRoleNames.Administrator);
        var petty = await EnsureUserAsync("demo.cajachica", "demo.cajachica@sistemi.local", "Demo Responsable Caja Chica", SecurityRoleNames.Administrator);

        var currencies = await EnsureCurrenciesAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var setting = await EnsureSystemSettingAsync(currencies["GTQ"], cancellationToken);
        var bankAccount = EnsureBankAccount();
        var posDevice = EnsurePosDevice();
        var categories = EnsureCategories();
        var pettyCategories = EnsurePettyCashCategories();
        var variants = EnsureVariantCatalog();
        var suppliers = EnsureSuppliers();
        var customers = EnsureCustomers();
        var products = EnsureProducts(categories, variants);
        EnsureBrands(products);

        await db.SaveChangesAsync(cancellationToken);

        var sessions = EnsureCashSessions(cashier, cashierTwo, manager, admin, posDevice);
        var purchases = EnsurePurchases(suppliers, products, purchasing.UserName!);
        EnsurePurchaseOrders(suppliers, products, purchasing.UserName!);
        var sales = EnsureSales(customers, products, sessions, bankAccount, posDevice, setting, cashier.UserName!, cashierTwo.UserName!);
        EnsureAccountPayments(sales.Receivables, purchases.Payables, sessions.Active, petty.UserName!);
        CloseHistoricalCashSessions(sessions, posDevice, manager.UserName!);
        EnsureCashCounts(sessions);
        EnsurePettyCash(petty, cashierTwo, pettyCategories);
        EnsureFinancialAudit(sessions, purchases.Payables, sales.Receivables, admin.UserName!, manager.UserName!);
        db.AuditLogs.Add(new AuditLog
        {
            Module = "Demo",
            Action = "Seed",
            RecordType = "DemoData",
            RecordId = "DEMO-SEED-COMPLETE",
            DocumentNumber = Prefix,
            UserName = "demo-seeder",
            OccurredAtUtc = BaseDate,
            ChangesJson = "{}"
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureMissingDemoReviewDataAsync(CancellationToken cancellationToken)
    {
        var changed = false;

        var exhaustedProduct = await db.Products.FirstOrDefaultAsync(x => x.InternalCode == $"{Prefix}P007", cancellationToken);
        if (exhaustedProduct is not null && !await db.InventoryStocks.AnyAsync(x => x.ProductId == exhaustedProduct.Id, cancellationToken))
        {
            db.InventoryStocks.Add(new InventoryStock(exhaustedProduct.Id));
            changed = true;
        }

        var lowStockProduct = await db.Products.FirstOrDefaultAsync(x => x.InternalCode == $"{Prefix}P003", cancellationToken);
        if (lowStockProduct is not null &&
            !await db.InventoryMovements.AnyAsync(x => x.SourceDocument == $"{Prefix}AJ-BAJO", cancellationToken))
        {
            var stock = await db.InventoryStocks.FirstOrDefaultAsync(x => x.ProductId == lowStockProduct.Id, cancellationToken);
            if (stock is not null && stock.CurrentStock > 4m)
            {
                var previous = stock.CurrentStock;
                stock.ReplaceForSystem(4m);
                db.InventoryMovements.Add(new InventoryMovement(
                    lowStockProduct.Id,
                    InventoryMovementType.NegativeAdjustment,
                    previous - stock.CurrentStock,
                    previous,
                    stock.CurrentStock,
                    BaseDate.AddMinutes(-20),
                    "demo.admin",
                    $"{Prefix}AJ-BAJO",
                    "Ajuste demo para mostrar producto con existencia baja."));
                changed = true;
            }
        }

        var sessions = await db.CashSessions
            .Include(x => x.Movements)
            .Where(x => x.RegisterCode.StartsWith(Prefix))
            .ToListAsync(cancellationToken);
        var countedSessionIds = await db.CashCounts
            .Where(x => sessions.Select(s => s.Id).Contains(x.CashSessionId))
            .Select(x => x.CashSessionId)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions.Where(x => !countedSessionIds.Contains(x.Id)))
        {
            AddCashCount(session);
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<AppUser> EnsureUserAsync(string userName, string email, string fullName, string roleName)
    {
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            user = new AppUser
            {
                UserName = userName,
                Email = email,
                FullName = fullName,
                EmailConfirmed = true,
                IsActive = true
            };

            var created = await userManager.CreateAsync(user, "Demo1234");
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", created.Errors.Select(x => x.Description)));
            }
        }

        if (!await userManager.IsInRoleAsync(user, roleName))
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                throw new InvalidOperationException($"El rol requerido no existe: {roleName}");
            }

            await userManager.AddToRoleAsync(user, roleName);
        }

        return user;
    }

    private async Task<Dictionary<string, CurrencyDefinition>> EnsureCurrenciesAsync(CancellationToken cancellationToken)
    {
        var desired = new[]
        {
            new CurrencyDefinition("GTQ", "Quetzal", "Q", 2, ".", ",", CurrencySymbolPosition.Before),
            new CurrencyDefinition("USD", "Dólar", "$", 2, ".", ",", CurrencySymbolPosition.Before),
            new CurrencyDefinition("MXN", "Peso mexicano", "$", 2, ".", ",", CurrencySymbolPosition.Before),
            new CurrencyDefinition("EUR", $"{Prefix}Euro", "EUR", 2, ",", ".", CurrencySymbolPosition.After),
            new CurrencyDefinition("HNL", $"{Prefix}Lempira", "L", 2, ".", ",", CurrencySymbolPosition.Before),
            new CurrencyDefinition("COP", $"{Prefix}Peso colombiano", "$", 2, ",", ".", CurrencySymbolPosition.Before)
        };

        var desiredCodes = desired.Select(x => x.Code).ToArray();
        var currencies = await db.Currencies
            .Where(x => desiredCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var currency in desired)
        {
            if (!currencies.ContainsKey(currency.Code))
            {
                db.Currencies.Add(currency);
                currencies.Add(currency.Code, currency);
            }
        }

        return currencies;
    }

    private async Task<SystemSetting> EnsureSystemSettingAsync(CurrencyDefinition currency, CancellationToken cancellationToken)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(cancellationToken);
        if (setting is null)
        {
            setting = SystemSetting.CreateDefault();
            db.SystemSettings.Add(setting);
        }

        setting.Update(
            $"{Prefix}Miscelanea La Esperanza",
            "DEMO-1010101",
            "4a avenida 2-15 zona 1, Jalapa",
            "7922-0101",
            setting.BusinessLogoUrl,
            currency.Code,
            currency.Name,
            currency.Symbol,
            currency.Decimals,
            currency.DecimalSeparator,
            currency.ThousandsSeparator,
            currency.SymbolPosition,
            12m,
            true,
            "Datos de demostracion. No usar para operacion real.",
            true,
            true,
            true,
            true,
            10m,
            NitLookupProviderType.None,
            30);
        setting.SelectCurrency(currency);
        return setting;
    }

    private BankAccount EnsureBankAccount()
    {
        var account = db.BankAccounts.Local.FirstOrDefault(x => x.AccountNumber == $"{Prefix}001-000123-4")
            ?? db.BankAccounts.FirstOrDefault(x => x.AccountNumber == $"{Prefix}001-000123-4");
        if (account is not null)
        {
            return account;
        }

        account = new BankAccount($"{Prefix}Banco Industrial", $"{Prefix}Cuenta Principal", $"{Prefix}001-000123-4");
        db.BankAccounts.Add(account);
        return account;
    }

    private PosDevice EnsurePosDevice()
    {
        var name = NameNormalizer.Normalize($"{Prefix}POS Mostrador 1");
        var device = db.PosDevices.Local.FirstOrDefault(x => NameNormalizer.Equals(x.Name, name))
            ?? db.PosDevices.FirstOrDefault(x => x.Name == name);
        if (device is not null)
        {
            return device;
        }

        device = new PosDevice(name);
        db.PosDevices.Add(device);
        return device;
    }

    private Dictionary<string, Category> EnsureCategories()
    {
        var data = new[]
        {
            ("Abarrotes", "Productos de consumo diario."),
            ("Bebidas", "Bebidas frias y de anaquel."),
            ("Limpieza", "Insumos de limpieza para hogar."),
            ("Ferreteria", "Productos basicos de ferreteria."),
            ("Papeleria", "Utiles escolares y oficina.")
        };

        return data.ToDictionary(x => x.Item1, x =>
        {
            var name = NameNormalizer.Normalize($"{Prefix}{x.Item1}");
            var category = db.Categories.Local.FirstOrDefault(c => NameNormalizer.Equals(c.Name, name))
                ?? db.Categories.FirstOrDefault(c => c.Name == name);
            if (category is not null)
            {
                return category;
            }

            category = new Category(name, x.Item2);
            db.Categories.Add(category);
            return category;
        });
    }

    private Dictionary<string, PettyCashCategory> EnsurePettyCashCategories()
    {
        var names = new[] { "Combustible", "Mensajeria", "Papeleria", "Mantenimiento", "Alimentacion" };
        return names.ToDictionary(x => x, x =>
        {
            var name = NameNormalizer.Normalize($"{Prefix}{x}");
            var category = db.PettyCashCategories.Local.FirstOrDefault(c => NameNormalizer.Equals(c.Name, name))
                ?? db.PettyCashCategories.FirstOrDefault(c => c.Name == name);
            if (category is not null)
            {
                return category;
            }

            category = new PettyCashCategory(name);
            db.PettyCashCategories.Add(category);
            return category;
        });
    }

    private Dictionary<string, VariantOption> EnsureVariantCatalog()
    {
        var result = new Dictionary<string, VariantOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var (typeName, values) in new[]
        {
            ("Tamano", new[] { "Pequeno", "Mediano", "Grande" }),
            ("Color", new[] { "Rojo", "Azul", "Negro" }),
            ("Presentacion", new[] { "Unidad", "Caja", "Paquete" })
        })
        {
            var name = NameNormalizer.Normalize($"{Prefix}{typeName}");
            var type = db.VariantTypes.Include(x => x.Options).FirstOrDefault(x => x.Name == name);
            if (type is null)
            {
                type = new VariantType(name);
                db.VariantTypes.Add(type);
            }

            foreach (var value in values)
            {
                result[$"{typeName}:{value}"] = type.GetOrAddOption($"{Prefix}{value}");
            }
        }

        return result;
    }

    private List<Supplier> EnsureSuppliers()
    {
        var data = new[]
        {
            ("Distribuidora El Progreso, S.A.", "DEMO-900001-1", "El Progreso", "Carlos Mendez"),
            ("Importadora Oriente, S.A.", "DEMO-900002-2", "Oriente", "Maria Aguilar"),
            ("Agroservicios Jalapa", "DEMO-900003-3", "Agro Jalapa", "Luis Perez"),
            ("Papeles y Mas, S.A.", "DEMO-900004-4", "Papeles y Mas", "Ana Morales"),
            ("Ferreteria Central, S.A.", "DEMO-900005-5", "Central", "Hugo Lopez")
        };

        return data.Select(x =>
        {
            var nit = Supplier.NormalizeNit(x.Item2);
            var supplier = db.Suppliers.Local.FirstOrDefault(s => s.Nit == nit)
                ?? db.Suppliers.FirstOrDefault(s => s.Nit == nit);
            if (supplier is not null)
            {
                return supplier;
            }

            supplier = new Supplier($"{Prefix}{x.Item1}", x.Item2, $"{Prefix}{x.Item3}", "7922-1000", $"compras+{x.Item2.Replace("-", "")}@demo.local", "Jalapa, Guatemala", x.Item4);
            db.Suppliers.Add(supplier);
            return supplier;
        }).ToList();
    }

    private List<Customer> EnsureCustomers()
    {
        var data = new[]
        {
            ("Tienda San Jose", "DEMO-800001-1", "7922-2001"),
            ("Colegio El Saber", "DEMO-800002-2", "7922-2002"),
            ("Clinica Santa Marta", "DEMO-800003-3", "7922-2003"),
            ("Constructora Los Pinos", "DEMO-800004-4", "7922-2004"),
            ("Consumidor Demo Frecuente", "DEMO-800005-5", "7922-2005")
        };

        return data.Select(x =>
        {
            var customer = db.Customers.Local.FirstOrDefault(c => c.Nit == x.Item2)
                ?? db.Customers.FirstOrDefault(c => c.Nit == x.Item2);
            if (customer is not null)
            {
                return customer;
            }

            customer = new Customer($"{Prefix}{x.Item1}", x.Item2, x.Item3, $"cliente+{x.Item2.Replace("-", "")}@demo.local", "Jalapa, Guatemala");
            db.Customers.Add(customer);
            return customer;
        }).ToList();
    }

    private List<Product> EnsureProducts(Dictionary<string, Category> categories, Dictionary<string, VariantOption> variants)
    {
        var data = new[]
        {
            ("P001", "7401001000011", "Arroz blanco premium 5 lb", "Abarrotes", "La Cosecha", "bolsa", 28m, 35m, 12m, true, "Presentacion:Unidad"),
            ("P002", "7401001000028", "Agua pura 600 ml", "Bebidas", "Fuente Clara", "unidad", 2.25m, 4m, 24m, true, "Tamano:Pequeno"),
            ("P003", "7401001000035", "Cafe tostado 400 g", "Abarrotes", "Cafe Volcan", "bolsa", 22m, 32m, 10m, true, "Presentacion:Paquete"),
            ("P004", "7401001000042", "Detergente limon 1 kg", "Limpieza", "Brillo Hogar", "bolsa", 13m, 19m, 8m, true, "Presentacion:Unidad"),
            ("P005", "7401001000059", "Cuaderno universitario 100 hojas", "Papeleria", "Estudio", "unidad", 6m, 10m, 15m, true, "Color:Azul"),
            ("P006", "7401001000066", "Bombillo LED 12W", "Ferreteria", "LuzPlus", "unidad", 14m, 24m, 6m, true, "Tamano:Mediano"),
            ("P007", "7401001000073", "Cable extension 3 m", "Ferreteria", "ElectroMax", "unidad", 24m, 39m, 5m, true, "Color:Negro"),
            ("P008", "7401001000080", "Marcador permanente negro", "Papeleria", "Trazo", "unidad", 3m, 6m, 20m, true, "Color:Negro")
        };

        return data.Select(x =>
        {
            var internalCode = $"{Prefix}{x.Item1}";
            var product = db.Products.Local.FirstOrDefault(p => p.InternalCode == internalCode)
                ?? db.Products.Include(p => p.VariantOptions).FirstOrDefault(p => p.InternalCode == internalCode);
            if (product is not null)
            {
                return product;
            }

            product = new Product(
                internalCode,
                x.Item2,
                $"{Prefix}{x.Item3}",
                "Producto creado para demostracion de flujos relacionados.",
                categories[x.Item4].Id,
                $"{Prefix}{x.Item5}",
                x.Item6,
                x.Item7,
                x.Item7,
                x.Item8,
                x.Item9,
                x.Item10);
            if (variants.TryGetValue(x.Item11, out var option))
            {
                product.AssignVariantOptions([(option.Id, 1)]);
            }

            db.Products.Add(product);
            return product;
        }).ToList();
    }

    private void EnsureBrands(IEnumerable<Product> products)
    {
        var existing = db.Brands.Local.Select(x => x.Name)
            .Concat(db.Brands.AsNoTracking().Select(x => x.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in products.Select(x => x.Brand).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (existing.Add(name))
                db.Brands.Add(new Brand(name));
        }
    }

    private DemoCashSessions EnsureCashSessions(AppUser cashier, AppUser cashierTwo, AppUser manager, AppUser admin, PosDevice posDevice)
    {
        var closedExact = new CashSession($"{Prefix}CAJA-01", cashier.UserName!, BaseDate.AddDays(-3), 500m, cashier.Id, "Apertura demo con cierre cuadrado.");
        var closedShort = new CashSession($"{Prefix}CAJA-02", manager.UserName!, BaseDate.AddDays(-2), 300m, manager.Id, "Apertura demo con diferencia.");
        var closedPos = new CashSession($"{Prefix}CAJA-03", admin.UserName!, BaseDate.AddDays(-1), 400m, admin.Id, "Apertura demo con POS.");
        var closedNoSales = new CashSession($"{Prefix}CAJA-04", cashier.UserName!, BaseDate.AddDays(-5), 250m, cashier.Id, "Caja demo sin ventas.");
        var active = new CashSession($"{Prefix}CAJA-05", cashierTwo.UserName!, BaseDate.AddHours(-4), 600m, cashierTwo.Id, "Caja demo abierta para revision.");

        AddCashMovement(closedExact, CashMovementType.OtherIncome, 50m, "Ingreso adicional demo", cashier.UserName!, BaseDate.AddDays(-3).AddHours(1));
        AddCashMovement(closedShort, CashMovementType.Expense, 25m, "Compra menor demo", manager.UserName!, BaseDate.AddDays(-2).AddHours(1));
        AddCashMovement(active, CashMovementType.OtherIncome, 75m, "Ingreso adicional demo", cashierTwo.UserName!, BaseDate.AddHours(-3));
        AddCashMovement(active, CashMovementType.Expense, 40m, "Gasto operativo demo", cashierTwo.UserName!, BaseDate.AddHours(-2));

        db.CashSessions.AddRange(closedNoSales, closedExact, closedShort, closedPos, active);
        return new DemoCashSessions(active, closedExact, closedShort, closedPos, closedNoSales);
    }

    private static void CloseHistoricalCashSessions(DemoCashSessions sessions, PosDevice posDevice, string managerUserName)
    {
        sessions.ClosedNoSales.Close(BaseDate.AddDays(-5).AddHours(8), 250m, null, "Cierre demo sin movimiento.");
        sessions.ClosedExact.Close(BaseDate.AddDays(-3).AddHours(8), sessions.ClosedExact.CalculateExpectedCash(), null, "Cierre demo cuadrado.");
        sessions.ClosedShort.Close(BaseDate.AddDays(-2).AddHours(8), sessions.ClosedShort.CalculateExpectedCash() - 10m, managerUserName, "Cierre demo con faltante autorizado.");

        var posClosing = new CashPosClosing(sessions.ClosedPos.Id, posDevice.Id, posDevice.Name, sessions.ClosedPos.TotalPosSales ?? 72m, sessions.ClosedPos.TotalPosSales ?? 72m, $"{Prefix}LOTE-0001", null);
        sessions.ClosedPos.AddPosClosing(posClosing);
        sessions.ClosedPos.Close(BaseDate.AddDays(-1).AddHours(8), sessions.ClosedPos.CalculateExpectedCash(), null, "Cierre demo POS.");
    }

    private void EnsureCashCounts(DemoCashSessions sessions)
    {
        AddCashCount(sessions.ClosedNoSales);
        AddCashCount(sessions.ClosedExact);
        AddCashCount(sessions.ClosedShort);
        AddCashCount(sessions.ClosedPos);
        AddCashCount(sessions.Active);
    }

    private void AddCashCount(CashSession session)
    {
        var expected = session.CountedCash ?? session.CalculateExpectedCash();
        var countedAt = (session.ClosedAtUtc ?? BaseDate).AddMinutes(-10);
        db.CashCounts.Add(new CashCount(session.Id, 1, countedAt, session.CashierUserName, expected, BuildCashCountLines(expected)));
    }

    private IReadOnlyList<CashCountLine> BuildCashCountLines(decimal amount)
    {
        var remaining = decimal.Round(amount, 2);
        var lines = new List<CashCountLine>();
        var denominations = db.CashDenominations
            .OrderByDescending(x => x.Value)
            .ToList();

        foreach (var denomination in denominations)
        {
            var quantity = (int)Math.Floor(remaining / denomination.Value);
            if (quantity <= 0)
            {
                continue;
            }

            lines.Add(new CashCountLine(denomination.Id, denomination.Value, quantity));
            remaining = decimal.Round(remaining - denomination.Value * quantity, 2);
        }

        if (remaining != 0)
        {
            throw new DomainRuleException("No fue posible representar el arqueo demo con las denominaciones configuradas.");
        }

        return lines;
    }

    private DemoPurchases EnsurePurchases(List<Supplier> suppliers, List<Product> products, string userName)
    {
        var purchaseOne = CreatePurchase($"{Prefix}C-0001", suppliers[0], userName, BaseDate.AddDays(-12), PurchaseTermsType.Cash, null,
            [(products[0], 80m, 28m), (products[1], 150m, 2.25m)]);
        var purchaseTwo = CreatePurchase($"{Prefix}C-0002", suppliers[1], userName, BaseDate.AddDays(-10), PurchaseTermsType.Credit, BaseDate.AddDays(20),
            [(products[2], 25m, 22m), (products[3], 35m, 13m)]);
        var purchaseThree = CreatePurchase($"{Prefix}C-0003", suppliers[2], userName, BaseDate.AddDays(-8), PurchaseTermsType.Credit, BaseDate.AddDays(10),
            [(products[4], 60m, 6m), (products[5], 30m, 14m)]);
        var purchaseFour = CreatePurchase($"{Prefix}C-0004", suppliers[3], userName, BaseDate.AddDays(-6), PurchaseTermsType.Credit, BaseDate.AddDays(-1),
            [(products[7], 100m, 3m)]);
        var draft = new Purchase($"{Prefix}C-0005", suppliers[4].Id, BaseDate.AddDays(-1), userName, [new PurchaseItem(products[6].Id, 12m, 24m)], PurchaseTermsType.Cash);

        var payables = new List<PayableAccount>();
        foreach (var purchase in new[] { purchaseOne, purchaseTwo, purchaseThree, purchaseFour })
        {
            ConfirmPurchase(purchase, userName);
            if (purchase.Terms == PurchaseTermsType.Credit)
            {
                var account = new PayableAccount(purchase.Id, purchase.SupplierId, purchase.Number, purchase.PurchaseDateUtc, purchase.DueDateUtc!.Value, purchase.Total);
                db.PayableAccounts.Add(account);
                payables.Add(account);
            }
        }

        db.Purchases.AddRange(purchaseOne, purchaseTwo, purchaseThree, purchaseFour, draft);
        return new DemoPurchases([purchaseOne, purchaseTwo, purchaseThree, purchaseFour, draft], payables);
    }

    private void EnsurePurchaseOrders(List<Supplier> suppliers, List<Product> products, string userName)
    {
        var requested = CreateOrder($"{Prefix}PC-0001", suppliers[0], userName, BaseDate.AddDays(-4), [(products[0], 20m), (products[1], 80m)]);
        var partial = CreateOrder($"{Prefix}PC-0002", suppliers[1], userName, BaseDate.AddDays(-7), [(products[2], 20m), (products[3], 20m)]);
        var received = CreateOrder($"{Prefix}PC-0003", suppliers[2], userName, BaseDate.AddDays(-9), [(products[4], 50m), (products[5], 15m)]);
        var cancelled = CreateOrder($"{Prefix}PC-0004", suppliers[3], userName, BaseDate.AddDays(-2), [(products[6], 10m)]);
        var requestedTwo = CreateOrder($"{Prefix}PC-0005", suppliers[4], userName, BaseDate.AddDays(-1), [(products[7], 40m)]);

        requested.Submit(BaseDate.AddDays(-4), userName);
        partial.Submit(BaseDate.AddDays(-7), userName);
        received.Submit(BaseDate.AddDays(-9), userName);
        received.CompleteReceipt(BaseDate.AddDays(-8), userName);
        foreach (var item in received.Items)
        {
            MoveStock(item.ProductId, InventoryMovementType.PurchaseEntry, item.RequestedQuantity, BaseDate.AddDays(-8), userName, received.Number, "Recepcion total demo.");
        }

        cancelled.Cancel();
        requestedTwo.Submit(BaseDate.AddDays(-1), userName);
        db.PurchaseOrders.AddRange(requested, partial, received, cancelled, requestedTwo);
    }

    private DemoSales EnsureSales(
        List<Customer> customers,
        List<Product> products,
        DemoCashSessions sessions,
        BankAccount bankAccount,
        PosDevice posDevice,
        SystemSetting setting,
        string cashierUserName,
        string activeCashierUserName)
    {
        var cashSale = CreateSale($"{Prefix}V-0001", sessions.ClosedExact, customers[4], cashierUserName, BaseDate.AddDays(-3).AddHours(2), setting,
            [new SaleItem(products[0].Id, products[0].DisplayName, 5m, products[0].SalePrice, 0m, setting.TaxPercent, !products[0].AppliesTax, products[0].LastCost)],
            SalePayment.CreateCash(175m, 200m, cashierUserName));
        AddSaleMovement(sessions.ClosedExact, cashSale, CashMovementType.SaleCash);

        var transferSale = CreateSale($"{Prefix}V-0002", sessions.ClosedPos, customers[0], cashierUserName, BaseDate.AddDays(-1).AddHours(2), setting,
            [new SaleItem(products[1].Id, products[1].DisplayName, 20m, products[1].SalePrice, 0m, setting.TaxPercent, !products[1].AppliesTax, products[1].LastCost)],
            SalePayment.CreateTransfer(80m, $"{Prefix}Banco del Cliente", bankAccount.Id, bankAccount.Bank, bankAccount.AccountName, bankAccount.AccountNumber, $"{Prefix}TR-0002", 80m, BaseDate.AddDays(-1).AddHours(2), TransferPaymentStatus.Verified, cashierUserName, $"{Prefix}Cliente remitente"));
        AddSaleMovement(sessions.ClosedPos, transferSale, CashMovementType.SaleTransfer);

        var posSale = CreateSale($"{Prefix}V-0003", sessions.ClosedPos, customers[1], cashierUserName, BaseDate.AddDays(-1).AddHours(3), setting,
            [new SaleItem(products[5].Id, products[5].DisplayName, 3m, products[5].SalePrice, 0m, setting.TaxPercent, !products[5].AppliesTax, products[5].LastCost)],
            SalePayment.CreatePos(72m, CardType.Debit, "1234", $"{Prefix}AUT-0003", $"{Prefix}VCH-0003", 72m, CardPaymentStatus.Approved, cashierUserName, posDevice.Id));
        AddSaleMovement(sessions.ClosedPos, posSale, CashMovementType.SalePos);

        var creditPending = CreateSale($"{Prefix}V-0004", sessions.Active, customers[2], activeCashierUserName, BaseDate.AddHours(-3), setting,
            [new SaleItem(products[3].Id, products[3].DisplayName, 4m, products[3].SalePrice, 0m, setting.TaxPercent, !products[3].AppliesTax, products[3].LastCost)],
            SalePayment.CreateCredit(76m, activeCashierUserName));
        var receivablePending = new ReceivableAccount(creditPending.Id, customers[2].Id, creditPending.Number, creditPending.SoldAtUtc, BaseDate.AddDays(15), creditPending.Total);

        var creditPartial = CreateSale($"{Prefix}V-0005", sessions.Active, customers[3], activeCashierUserName, BaseDate.AddHours(-2), setting,
            [new SaleItem(products[4].Id, products[4].DisplayName, 12m, products[4].SalePrice, 0m, setting.TaxPercent, !products[4].AppliesTax, products[4].LastCost)],
            SalePayment.CreateCredit(120m, activeCashierUserName));
        var receivablePartial = new ReceivableAccount(creditPartial.Id, customers[3].Id, creditPartial.Number, creditPartial.SoldAtUtc, BaseDate.AddDays(20), creditPartial.Total);

        var creditPaid = CreateSale($"{Prefix}V-0006", sessions.Active, customers[0], activeCashierUserName, BaseDate.AddHours(-1), setting,
            [new SaleItem(products[7].Id, products[7].DisplayName, 10m, products[7].SalePrice, 0m, setting.TaxPercent, !products[7].AppliesTax, products[7].LastCost)],
            SalePayment.CreateCredit(60m, activeCashierUserName));
        var receivablePaid = new ReceivableAccount(creditPaid.Id, customers[0].Id, creditPaid.Number, creditPaid.SoldAtUtc, BaseDate.AddDays(10), creditPaid.Total);

        var activeCash = CreateSale($"{Prefix}V-0007", sessions.Active, customers[4], activeCashierUserName, BaseDate.AddMinutes(-30), setting,
            [new SaleItem(products[1].Id, products[1].DisplayName, 10m, products[1].SalePrice, 0m, setting.TaxPercent, !products[1].AppliesTax, products[1].LastCost)],
            SalePayment.CreateCash(40m, 50m, activeCashierUserName));
        AddSaleMovement(sessions.Active, activeCash, CashMovementType.SaleCash);

        db.Sales.AddRange(cashSale, transferSale, posSale, creditPending, creditPartial, creditPaid, activeCash);
        db.ReceivableAccounts.AddRange(receivablePending, receivablePartial, receivablePaid);
        return new DemoSales([cashSale, transferSale, posSale, creditPending, creditPartial, creditPaid, activeCash], [receivablePending, receivablePartial, receivablePaid]);
    }

    private void EnsureAccountPayments(List<ReceivableAccount> receivables, List<PayableAccount> payables, CashSession activeSession, string userName)
    {
        var partialReceivable = receivables.Single(x => x.SaleNumber == $"{Prefix}V-0005");
        var paidReceivable = receivables.Single(x => x.SaleNumber == $"{Prefix}V-0006");
        var partialPayment = new ReceivablePayment(BaseDate.AddMinutes(-50), 45m, FinancialPaymentMethod.Cash, $"{Prefix}AB-0001", userName, null, activeSession.Id);
        var partialMovement = new CashMovement(CashMovementType.CollectionCash, 45m, $"Abono {partialReceivable.SaleNumber}", userName, BaseDate.AddMinutes(-50), null, nameof(ReceivableAccount), partialReceivable.Id, partialReceivable.SaleNumber) { CashSessionId = activeSession.Id };
        activeSession.AddMovement(partialMovement);
        partialPayment.CashMovementId = partialMovement.Id;
        partialReceivable.AddPayment(partialPayment);

        var paidPayment = new ReceivablePayment(BaseDate.AddMinutes(-40), paidReceivable.Total, FinancialPaymentMethod.Transfer, $"{Prefix}AB-0002", userName, null, null);
        paidReceivable.AddPayment(paidPayment);

        var partialPayable = payables.Single(x => x.InvoiceNumber == $"{Prefix}C-0002");
        partialPayable.AddPayment(new PayablePayment(BaseDate.AddDays(-2), 200m, FinancialPaymentMethod.Transfer, MoneySourceType.BankAccount, null, $"{Prefix}PG-0001", userName, null));

        var paidPayable = payables.Single(x => x.InvoiceNumber == $"{Prefix}C-0003");
        paidPayable.AddPayment(new PayablePayment(BaseDate.AddDays(-1), paidPayable.Total, FinancialPaymentMethod.Transfer, MoneySourceType.BankAccount, null, $"{Prefix}PG-0002", userName, null));
    }

    private void EnsurePettyCash(AppUser petty, AppUser cashierTwo, Dictionary<string, PettyCashCategory> categories)
    {
        var openFund = new PettyCashFund($"{Prefix}Caja chica Sucursal Jalapa", petty.Id, petty.UserName!);
        var openCycle = openFund.Open(1000m, petty.Id, petty.UserName!, BaseDate.AddDays(-1));
        db.PettyCashCycles.Add(openCycle);
        AddPettyMovement(openFund, PettyCashMovementType.Expense, 85m, "Compra de timbres fiscales", categories["Papeleria"], $"{Prefix}FAC-CC-001", "SAT", "Tramite administrativo demo.", petty.UserName!, BaseDate.AddHours(-6));
        AddPettyMovement(openFund, PettyCashMovementType.Expense, 120m, "Combustible mensajeria", categories["Combustible"], $"{Prefix}FAC-CC-002", "Gasolinera Jalapa", "Ruta a proveedor demo.", petty.UserName!, BaseDate.AddHours(-5));
        AddPettyMovement(openFund, PettyCashMovementType.Expense, 35m, "Envio de documentos", categories["Mensajeria"], $"{Prefix}REC-CC-003", "Mensajeria local", null, petty.UserName!, BaseDate.AddHours(-4));
        AddPettyMovement(openFund, PettyCashMovementType.Expense, 60m, "Reparacion de chapa", categories["Mantenimiento"], $"{Prefix}FAC-CC-004", "Cerrajeria Central", null, petty.UserName!, BaseDate.AddHours(-3));
        AddPettyMovement(openFund, PettyCashMovementType.Replenishment, 300m, "Reposicion parcial", categories["Alimentacion"], $"{Prefix}REP-CC-005", "Administracion", "Reposicion demo.", petty.UserName!, BaseDate.AddHours(-2));

        var closedFund = new PettyCashFund($"{Prefix}Caja chica Sucursal Jutiapa", cashierTwo.Id, cashierTwo.UserName!);
        var closedCycle = closedFund.Open(700m, cashierTwo.Id, cashierTwo.UserName!, BaseDate.AddDays(-6));
        db.PettyCashCycles.Add(closedCycle);
        AddPettyMovement(closedFund, PettyCashMovementType.Expense, 90m, "Viaticos ruta proveedores", categories["Alimentacion"], $"{Prefix}FAC-CC-006", "Comedor Central", null, cashierTwo.UserName!, BaseDate.AddDays(-6).AddHours(2));
        closedFund.Close(closedCycle, closedFund.Balance, cashierTwo.Id, cashierTwo.UserName!, BaseDate.AddDays(-6).AddHours(8), "Liquidacion demo cerrada.");
        db.PettyCashSettlements.Add(new PettyCashSettlement(closedFund.Id, closedCycle.ExpectedBalance ?? closedFund.Balance, closedFund.Balance, cashierTwo.UserName!, BaseDate.AddDays(-6).AddHours(8), "Liquidacion demo cerrada."));

        db.PettyCashFunds.AddRange(openFund, closedFund);
    }

    private void EnsureFinancialAudit(DemoCashSessions sessions, List<PayableAccount> payables, List<ReceivableAccount> receivables, string adminUserName, string managerUserName)
    {
        db.FinancialAuditEntries.AddRange(
            new FinancialAuditEntry { OccurredAtUtc = BaseDate.AddDays(-3).AddHours(8), UserName = adminUserName, Action = $"{Prefix}Cierre de caja revisado", CashSessionId = sessions.ClosedExact.Id, DocumentType = nameof(CashSession), DocumentId = sessions.ClosedExact.Id, DocumentReference = sessions.ClosedExact.RegisterCode, Amount = sessions.ClosedExact.CountedCash, Comment = "Caja demo cuadrada." },
            new FinancialAuditEntry { OccurredAtUtc = BaseDate.AddDays(-2).AddHours(8), UserName = managerUserName, Action = $"{Prefix}Diferencia de caja autorizada", CashSessionId = sessions.ClosedShort.Id, DocumentType = nameof(CashSession), DocumentId = sessions.ClosedShort.Id, DocumentReference = sessions.ClosedShort.RegisterCode, Amount = sessions.ClosedShort.Difference, AuthorizedByUserName = managerUserName, Comment = "Faltante demo autorizado." },
            new FinancialAuditEntry { OccurredAtUtc = BaseDate.AddDays(-1).AddHours(8), UserName = adminUserName, Action = $"{Prefix}Cierre POS conciliado", CashSessionId = sessions.ClosedPos.Id, DocumentType = nameof(CashSession), DocumentId = sessions.ClosedPos.Id, DocumentReference = sessions.ClosedPos.RegisterCode, Amount = sessions.ClosedPos.TotalPosSales, Comment = "Lote POS demo conciliado." },
            new FinancialAuditEntry { OccurredAtUtc = BaseDate.AddDays(-1), UserName = managerUserName, Action = $"{Prefix}Pago proveedor", DocumentType = nameof(PayableAccount), DocumentId = payables[1].Id, DocumentReference = payables[1].InvoiceNumber, Amount = payables[1].Paid, Comment = "Pago demo de cuenta por pagar." },
            new FinancialAuditEntry { OccurredAtUtc = BaseDate.AddHours(-1), UserName = managerUserName, Action = $"{Prefix}Abono cliente", CashSessionId = sessions.Active.Id, DocumentType = nameof(ReceivableAccount), DocumentId = receivables[1].Id, DocumentReference = receivables[1].SaleNumber, Amount = receivables[1].Paid, Comment = "Abono demo de cuenta por cobrar." });
    }

    private static Purchase CreatePurchase(string number, Supplier supplier, string userName, DateTimeOffset date, PurchaseTermsType terms, DateTimeOffset? dueDate, IReadOnlyList<(Product Product, decimal Quantity, decimal Cost)> items) =>
        new(number, supplier.Id, date, userName, items.Select(x => new PurchaseItem(x.Product.Id, x.Quantity, x.Cost)), terms, dueDate);

    private static PurchaseOrder CreateOrder(string number, Supplier supplier, string userName, DateTimeOffset date, IReadOnlyList<(Product Product, decimal Quantity)> items) =>
        new(number, supplier.Id, date, userName, items.Select(x => new PurchaseOrderItem(x.Product.Id, x.Quantity)));

    private void ConfirmPurchase(Purchase purchase, string userName)
    {
        foreach (var item in purchase.Items)
        {
            MoveStock(item.ProductId, InventoryMovementType.PurchaseEntry, item.Quantity, purchase.PurchaseDateUtc, userName, purchase.Number, "Entrada por compra demo.");
        }

        purchase.Confirm();
    }

    private Sale CreateSale(string number, CashSession session, Customer customer, string userName, DateTimeOffset soldAt, SystemSetting setting, IReadOnlyList<SaleItem> items, SalePayment payment)
    {
        var sale = new Sale(number, session.Id, customer.Id, soldAt, userName, items, payment, setting.CurrencyCode, setting.CurrencyName, setting.CurrencySymbol, setting.CurrencyDecimals, setting.DecimalSeparator, setting.ThousandsSeparator, setting.CurrencySymbolPosition.ToString());
        payment.SaleId = sale.Id;
        foreach (var item in items)
        {
            MoveStock(item.ProductId, InventoryMovementType.SaleExit, item.Quantity, soldAt, userName, sale.Number, "Salida por venta demo.", subtract: true);
        }

        return sale;
    }

    private void MoveStock(Guid productId, InventoryMovementType type, decimal quantity, DateTimeOffset at, string userName, string sourceDocument, string note, bool subtract = false)
    {
        var stock = db.InventoryStocks.Local.FirstOrDefault(x => x.ProductId == productId)
            ?? db.InventoryStocks.FirstOrDefault(x => x.ProductId == productId);
        if (stock is null)
        {
            stock = new InventoryStock(productId);
            db.InventoryStocks.Add(stock);
        }

        var previous = stock.CurrentStock;
        var next = subtract ? previous - quantity : previous + quantity;
        if (next < 0)
        {
            throw new DomainRuleException("La semilla demo intento dejar inventario negativo.");
        }

        stock.ReplaceForSystem(next);
        db.InventoryMovements.Add(new InventoryMovement(productId, type, quantity, previous, stock.CurrentStock, at, userName, sourceDocument, note));
    }

    private static void AddCashMovement(CashSession session, CashMovementType type, decimal amount, string reason, string userName, DateTimeOffset at)
    {
        var movement = new CashMovement(type, amount, $"{Prefix}{reason}", userName, at) { CashSessionId = session.Id };
        session.AddMovement(movement);
    }

    private static void AddSaleMovement(CashSession session, Sale sale, CashMovementType type)
    {
        var movement = new CashMovement(type, sale.Total, $"Venta {sale.Number}", sale.CashierUserName, sale.SoldAtUtc, null, nameof(Sale), sale.Id, sale.Number) { CashSessionId = session.Id };
        session.AddMovement(movement);
    }

    private static void AddPettyMovement(PettyCashFund fund, PettyCashMovementType type, decimal amount, string concept, PettyCashCategory category, string document, string beneficiary, string? observation, string userName, DateTimeOffset at)
    {
        var movement = new PettyCashMovement(type, amount, $"{Prefix}{concept}", category.Name, document, beneficiary, observation, userName, at, cycleId: fund.CurrentCycleId, categoryId: category.Id)
        {
            PettyCashFundId = fund.Id
        };
        fund.AddMovement(movement);
    }

    private sealed record DemoCashSessions(CashSession Active, CashSession ClosedExact, CashSession ClosedShort, CashSession ClosedPos, CashSession ClosedNoSales);
    private sealed record DemoPurchases(List<Purchase> Purchases, List<PayableAccount> Payables);
    private sealed record DemoSales(List<Sale> Sales, List<ReceivableAccount> Receivables);
}
