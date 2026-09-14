namespace TravelExpense.Api.Data.Entities;

public static class TourPlanStatus
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Closed = "Closed";
}

public class TourPlan : ICreationTimestamped, IUpdateTimestamped
{
    public int TourPlanId { get; set; }
    public int EmployeeId { get; set; }
    public int ChannelId { get; set; }
    public string Title { get; set; } = default!;
    public string? PurposeOfVisit { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = TourPlanStatus.Submitted;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Employee Employee { get; set; } = default!;
    public Channel Channel { get; set; } = default!;
    public ICollection<TourPlanStop> Stops { get; set; } = new List<TourPlanStop>();
    public ICollection<AdvanceRequest> AdvanceRequests { get; set; } = new List<AdvanceRequest>();
    public ICollection<ExpenseReport> ExpenseReports { get; set; } = new List<ExpenseReport>();
    public ICollection<DailyTourReport> DailyReports { get; set; } = new List<DailyTourReport>();

    void ICreationTimestamped.StampCreated(DateTime utcNow) => CreatedAt = utcNow;
    void IUpdateTimestamped.StampUpdated(DateTime utcNow) => UpdatedAt = utcNow;
}

public class TourPlanStop
{
    public int TourPlanStopId { get; set; }
    public int TourPlanId { get; set; }
    public DateOnly VisitDate { get; set; }
    public string Location { get; set; } = default!;
    public string? StateOrCountry { get; set; }
    public string? PurposeNotes { get; set; }

    public TourPlan TourPlan { get; set; } = default!;
}
