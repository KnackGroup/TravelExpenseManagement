namespace TravelExpense.Api.Data.Entities;

/// <summary>Keyless entities mapped onto the reconciliation views defined in db/schema.sql
/// (dbo.vw_TripFinancialSummary, dbo.vw_PendingPolicyExceptions). Read-only.</summary>
public class TripFinancialSummaryRow
{
    public int TourPlanId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = default!;
    public string RoleName { get; set; } = default!;
    public string ChannelName { get; set; } = default!;
    public string Title { get; set; } = default!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string TourPlanStatus { get; set; } = default!;
    public decimal TotalAdvanceDisbursed { get; set; }
    public decimal TotalExpenseClaimed { get; set; }
    public decimal TotalExpenseApproved { get; set; }
    public bool HasPolicyExceptions { get; set; }
    public decimal SettlementAmount { get; set; }
    public int? DailyReportsSubmitted { get; set; }
    public string? LastOutcomeSummary { get; set; }
}

public class PendingPolicyExceptionRow
{
    public int ExpenseLineItemId { get; set; }
    public int ExpenseReportId { get; set; }
    public int TourPlanId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = default!;
    public string CategoryName { get; set; } = default!;
    public DateOnly ExpenseDate { get; set; }
    public decimal ClaimedAmount { get; set; }
    public decimal? PolicyCapAmountApplied { get; set; }
    public string? ExceptionReason { get; set; }
    public string LineStatus { get; set; } = default!;
}
