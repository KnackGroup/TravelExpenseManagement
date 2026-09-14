namespace TravelExpense.Api.Data.Entities;

public class DailyTourReport : ICreationTimestamped
{
    public int DailyTourReportId { get; set; }
    public int TourPlanId { get; set; }
    public int EmployeeId { get; set; }
    public DateOnly ReportDate { get; set; }
    public string? VisitedLocations { get; set; }
    public string? WorkSummary { get; set; }
    public string? OutcomeSummary { get; set; }
    public string? NextDayPlan { get; set; }
    public DateTime CreatedAt { get; set; }

    public TourPlan TourPlan { get; set; } = default!;
    public Employee Employee { get; set; } = default!;

    void ICreationTimestamped.StampCreated(DateTime utcNow) => CreatedAt = utcNow;
}
