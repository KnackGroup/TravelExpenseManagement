namespace TravelExpense.Api.Dtos;

public record CreateAdvanceRequest(int TourPlanId, decimal RequestedAmount, string? RequestNotes);

public record AdvanceDecisionRequest(bool Approve, decimal? ApprovedAmount, string? Comments);

public record DisburseAdvanceRequest(string DisbursementMode, decimal Amount, string? ReferenceNo);

public record AdvanceApprovalDto(int AdvanceApprovalId, string ApproverName, string ApprovalStage, string Decision, decimal? ApprovedAmount, string? Comments, DateTime DecidedAt);

public record AdvanceDisbursementDto(string DisbursementMode, decimal Amount, string? ReferenceNo, string DisbursedByName, DateTime DisbursedAt);

public record AdvanceRequestDto(
    int AdvanceRequestId, int TourPlanId, string TourPlanTitle, int EmployeeId, string EmployeeName,
    decimal RequestedAmount, string Currency, string? RequestNotes, string Status,
    DateTime CreatedAt, List<AdvanceApprovalDto> Approvals, AdvanceDisbursementDto? Disbursement);
