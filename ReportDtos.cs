namespace TravelExpense.Api.Dtos;

public record TripFinancialSummaryDto(
    int TourPlanId, int EmployeeId, string EmployeeName, string RoleName, string ChannelName,
    string Title, DateOnly StartDate, DateOnly EndDate, string TourPlanStatus,
    decimal TotalAdvanceDisbursed, decimal TotalExpenseClaimed, decimal TotalExpenseApproved,
    bool HasPolicyExceptions, decimal SettlementAmount, int DailyReportsSubmitted, string? LastOutcomeSummary);

public record PendingPolicyExceptionDto(
    int ExpenseLineItemId, int ExpenseReportId, int TourPlanId, int EmployeeId, string EmployeeName,
    string CategoryName, DateOnly ExpenseDate, decimal ClaimedAmount, decimal? PolicyCapAmountApplied,
    string? ExceptionReason, string LineStatus);
