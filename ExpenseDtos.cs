namespace TravelExpense.Api.Dtos;

public record ExpenseLineItemRequest(
    DateOnly ExpenseDate, int ExpenseCategoryId, string? Description, decimal ClaimedAmount, string Currency);

public record CreateExpenseReportRequest(int TourPlanId, List<ExpenseLineItemRequest> LineItems);

public record ExpenseLineItemDto(
    int ExpenseLineItemId, DateOnly ExpenseDate, int ExpenseCategoryId, string CategoryName,
    string? Description, decimal ClaimedAmount, string Currency,
    decimal? PolicyCapAmountApplied, bool IsPolicyException, string? ExceptionReason,
    string LineStatus, decimal? ApprovedAmount, string? ApproverComments);

public record ExpenseReportDto(
    int ExpenseReportId, int TourPlanId, string TourPlanTitle, int EmployeeId, string EmployeeName,
    DateTime? SubmittedAt, string Status, decimal TotalClaimedAmount, decimal? TotalApprovedAmount,
    bool HasPolicyExceptions, List<ExpenseLineItemDto> LineItems);

public record LineItemDecisionRequest(bool Approve, decimal? ApprovedAmount, string? Comments);

public record ExpenseReportDecisionRequest(List<LineItemDecisionEntry> Decisions);
public record LineItemDecisionEntry(int ExpenseLineItemId, bool Approve, decimal? ApprovedAmount, string? Comments);
