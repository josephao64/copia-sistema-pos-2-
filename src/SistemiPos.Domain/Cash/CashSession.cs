using SistemiPos.Domain.Common;
using SistemiPos.Domain.Common.Exceptions;
using SistemiPos.Domain.Organizations;

namespace SistemiPos.Domain.Cash;

public sealed class CashSession : AuditableEntity
{
    private readonly List<CashMovement> _movements = [];
    private readonly List<CashCount> _counts = [];
    private readonly List<CashPosClosing> _posClosings = [];
    private readonly List<CashSessionPosDevice> _assignedPosDevices = [];

    public Guid? BranchId { get; private set; }
    public Branch? Branch { get; set; }
    public string RegisterCode { get; private set; } = string.Empty;
    public string CashierUserName { get; private set; } = string.Empty;
    public Guid? CashierUserId { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public decimal OpeningAmount { get; private set; }
    public string? OpeningObservation { get; private set; }
    public CashSessionStatus Status { get; private set; } = CashSessionStatus.Active;
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public decimal? CountedCash { get; private set; }
    public decimal? TotalSold { get; private set; }
    public decimal? TotalCashSales { get; private set; }
    public decimal? TotalTransferSales { get; private set; }
    public decimal? TotalPosSales { get; private set; }
    public decimal? OtherIncomeTotal { get; private set; }
    public decimal? ExpenseTotal { get; private set; }
    public decimal? TotalCashRefunds { get; private set; }
    public decimal? TotalTransferRefunds { get; private set; }
    public decimal? TotalPosRefunds { get; private set; }
    public decimal? ExpectedCash { get; private set; }
    public decimal? Difference { get; private set; }
    public IReadOnlyCollection<CashMovement> Movements => _movements;
    public IReadOnlyCollection<CashCount> Counts => _counts;
    public IReadOnlyCollection<CashPosClosing> PosClosings => _posClosings;
    public IReadOnlyCollection<CashSessionPosDevice> AssignedPosDevices => _assignedPosDevices;
    public string? DifferenceAuthorizedByUserName { get; private set; }
    public string? ClosingObservation { get; private set; }
    public DateTimeOffset? ReconciledAtUtc { get; private set; }
    public string? ReconciledByUserName { get; private set; }
    public string? ReconciliationObservation { get; private set; }

    private CashSession()
    {
    }

    public CashSession(
        string registerCode,
        string cashierUserName,
        DateTimeOffset openedAtUtc,
        decimal openingAmount,
        Guid? cashierUserId = null,
        string? openingObservation = null,
        Guid? branchId = null)
    {
        if (string.IsNullOrWhiteSpace(registerCode))
        {
            throw new DomainRuleException("El punto de caja es requerido.");
        }

        if (openingAmount < 0)
        {
            throw new DomainRuleException("El monto inicial no puede ser negativo.");
        }

        RegisterCode = registerCode.Trim();
        CashierUserName = string.IsNullOrWhiteSpace(cashierUserName) ? "system" : cashierUserName.Trim();
        CashierUserId = cashierUserId;
        OpenedAtUtc = openedAtUtc;
        OpeningAmount = decimal.Round(openingAmount, 2);
        OpeningObservation = string.IsNullOrWhiteSpace(openingObservation) ? null : openingObservation.Trim();
        BranchId = branchId;
    }

    public void AssignBranch(Guid branchId)
    {
        BranchId = branchId;
    }

    public void AddMovement(CashMovement movement)
    {
        if (Status != CashSessionStatus.Active)
        {
            throw new DomainRuleException("Solo se pueden registrar ingresos y egresos en una caja activa.");
        }

        if (movement.Type is not CashMovementType.SaleCash
            and not CashMovementType.SaleTransfer
            and not CashMovementType.SalePos
            and not CashMovementType.OtherIncome
            and not CashMovementType.Expense
            and not CashMovementType.RefundCash
            and not CashMovementType.RefundTransfer
            and not CashMovementType.RefundPos
            and not CashMovementType.CollectionCash
            and not CashMovementType.SupplierPayment
            and not CashMovementType.CashWithdrawal
            and not CashMovementType.CashTransfer)
        {
            throw new DomainRuleException("El movimiento no es válido para una caja activa.");
        }

        _movements.Add(movement);
    }

    public void AddCount(CashCount count)
    {
        EnsureActive();
        if (_counts.Count > 0)
        {
            throw new DomainRuleException("Esta caja ya tiene registrado su arqueo de cierre.");
        }
        if (count.CashSessionId != Id)
        {
            throw new DomainRuleException("El arqueo no pertenece a esta caja.");
        }

        _counts.Add(count);
    }

    public void AssignPosDevice(Guid posDeviceId, string posDeviceName)
    {
        EnsureActive();
        if (_assignedPosDevices.Any(x => x.PosDeviceId == posDeviceId)) return;
        _assignedPosDevices.Add(new CashSessionPosDevice(Id, posDeviceId, posDeviceName));
    }

    public void AddPosClosing(CashPosClosing closing)
    {
        EnsureActive();
        if (closing.CashSessionId != Id)
        {
            throw new DomainRuleException("El cierre POS no pertenece a esta caja.");
        }

        _posClosings.Add(closing);
    }

    public void AddAdjustment(CashMovement adjustment)
    {
        if (Status != CashSessionStatus.Closed)
        {
            throw new DomainRuleException("Los ajustes solo se permiten después del cierre.");
        }

        if (adjustment.Type is not CashMovementType.AdjustmentIncrease and not CashMovementType.AdjustmentDecrease)
        {
            throw new DomainRuleException("El tipo de ajuste no es válido.");
        }

        _movements.Add(adjustment);
    }

    public void Close(
        DateTimeOffset closedAtUtc,
        decimal countedCash,
        string? differenceAuthorizedByUserName = null,
        string? closingObservation = null,
        string? reconciledByUserName = null)
    {
        if (Status == CashSessionStatus.Closed)
        {
            throw new DomainRuleException("La caja ya fue cerrada.");
        }

        if (countedCash < 0)
        {
            throw new DomainRuleException("El efectivo contado no puede ser negativo.");
        }

        var saleCash = Sum(CashMovementType.SaleCash);
        var saleTransfer = Sum(CashMovementType.SaleTransfer);
        var salePos = Sum(CashMovementType.SalePos);
        var otherIncome = Sum(CashMovementType.OtherIncome);
        var expense = Sum(CashMovementType.Expense);
        var refundCash = Sum(CashMovementType.RefundCash);
        var refundTransfer = Sum(CashMovementType.RefundTransfer);
        var refundPos = Sum(CashMovementType.RefundPos);
        var collectionCash = Sum(CashMovementType.CollectionCash);
        var supplierPayments = Sum(CashMovementType.SupplierPayment);
        var withdrawals = Sum(CashMovementType.CashWithdrawal);
        var transfers = Sum(CashMovementType.CashTransfer);
        var expectedCash = OpeningAmount + saleCash + otherIncome + collectionCash - expense - refundCash - supplierPayments - withdrawals - transfers;
        var totalSold = saleCash + saleTransfer + salePos;

        ClosedAtUtc = closedAtUtc;
        CountedCash = decimal.Round(countedCash, 2);
        TotalSold = decimal.Round(totalSold, 2);
        TotalCashSales = decimal.Round(saleCash, 2);
        TotalTransferSales = decimal.Round(saleTransfer, 2);
        TotalPosSales = decimal.Round(salePos, 2);
        OtherIncomeTotal = decimal.Round(otherIncome, 2);
        ExpenseTotal = decimal.Round(expense, 2);
        TotalCashRefunds = decimal.Round(refundCash, 2);
        TotalTransferRefunds = decimal.Round(refundTransfer, 2);
        TotalPosRefunds = decimal.Round(refundPos, 2);
        ExpectedCash = decimal.Round(expectedCash, 2);
        Difference = decimal.Round(CountedCash.Value - ExpectedCash.Value, 2);
        DifferenceAuthorizedByUserName = string.IsNullOrWhiteSpace(differenceAuthorizedByUserName) ? null : differenceAuthorizedByUserName.Trim();
        ClosingObservation = string.IsNullOrWhiteSpace(closingObservation) ? null : closingObservation.Trim();
        Status = CashSessionStatus.Closed;

        if (Difference == 0m && !string.IsNullOrWhiteSpace(reconciledByUserName))
        {
            ReconciledAtUtc = closedAtUtc;
            ReconciledByUserName = reconciledByUserName.Trim();
            ReconciliationObservation = "Conciliación automática: cierre sin diferencia.";
        }
    }

    public decimal CalculateExpectedCash() => decimal.Round(
        OpeningAmount
        + Sum(CashMovementType.SaleCash)
        + Sum(CashMovementType.OtherIncome)
        + Sum(CashMovementType.CollectionCash)
        - Sum(CashMovementType.Expense)
        - Sum(CashMovementType.RefundCash)
        - Sum(CashMovementType.SupplierPayment)
        - Sum(CashMovementType.CashWithdrawal)
        - Sum(CashMovementType.CashTransfer), 2);

    public void MarkReconciled(DateTimeOffset reconciledAtUtc, string userName, string? observation = null)
    {
        if (Status != CashSessionStatus.Closed)
            throw new DomainRuleException("Solo se puede cuadrar una caja cerrada.");
        if (Difference is null or 0)
            throw new DomainRuleException("La caja no tiene una diferencia pendiente.");
        if (ReconciledAtUtc is not null)
            throw new DomainRuleException("La diferencia de esta caja ya fue cuadrada.");
        if (string.IsNullOrWhiteSpace(userName))
            throw new DomainRuleException("El usuario que revisa la caja es requerido.");

        ReconciledAtUtc = reconciledAtUtc;
        ReconciledByUserName = userName.Trim();
        ReconciliationObservation = string.IsNullOrWhiteSpace(observation) ? null : observation.Trim();
    }

    private void EnsureActive()
    {
        if (Status != CashSessionStatus.Active)
        {
            throw new DomainRuleException("La caja cerrada es inmutable.");
        }
    }

    private decimal Sum(CashMovementType type) => _movements.Where(x => x.Type == type).Sum(x => x.Amount);
}
