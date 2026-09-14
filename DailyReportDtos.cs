namespace TravelExpense.Api.Dtos;

public record CreateDailyReportRequest(
    int TourPlanId, DateOnly ReportDate, string? VisitedLocations,
    string? WorkSummary, string? OutcomeSummary, string? NextDayPlan);

public record DailyReportDto(
    int DailyTourReportId, int TourPlanId, int EmployeeId, string EmployeeName, DateOnly ReportDate,
    string? VisitedLocations, string? WorkSummary, string? OutcomeSummary, string? NextDayPlan, DateTime CreatedAt);
