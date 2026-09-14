namespace TravelExpense.Api.Data.Entities;

public static class ExpenseReportStatus
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string UnderReview = "UnderReview";
    public const string Approved = "Approved";
    public const string PartiallyApproved = "PartiallyApproved";
    public const string Rejected = "Rejected";
}

public static class LineStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public class ExpenseReport : ICreationTimestamped, IUpdateTimestamped
{
    public int ExpenseReportId { get; set; }
    public int TourPlanId { get; set; }
    public int EmployeeId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string Status { get; set; } = ExpenseReportStatus.Draft;
    public decimal TotalClaimedAmount { get; set; }
    public decimal? TotalApprovedAmount { get; set; }
    public bool HasPolicyExceptions { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public TourPlan TourPlan { get; set; } = default!;
    public Employee Employee { get; set; } = default!;
    public ICollection<ExpenseLineItem> LineItems { get; set; } = new List<ExpenseLineItem>();

    void ICreationTimestamped.StampCreated(DateTime utcNow) => CreatedAt = utcNow;
    void IUpdateTimestamped.StampUpdated(DateTime utcNow) => UpdatedAt = utcNow;
}

public class ExpenseLineItem : ICreationTimestamped
{
    public int ExpenseLineItemId { get; set; }
    public int ExpenseReportId { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public int ExpenseCategoryId { get; set; }
    public string? Description { get; set; }
    public decimal ClaimedAmount { get; set; }
    public string Currency { get; set; } = "INR";
    public decimal? PolicyCapAmountApplied { get; set; }
    public bool IsPolicyException { get; set; }
    public string? ExceptionReason { get; set; }
    public string LineStatus { get; set; } = Entities.LineStatus.Pending;
    public decimal? ApprovedAmount { get; set; }
    public int? ApproverEmployeeId { get; set; }
    public string? ApproverComments { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public ExpenseReport ExpenseReport { get; set; } = default!;
    public ExpenseCategory ExpenseCategory { get; set; } = default!;
    public Employee? Approver { get; set; }

    void ICreationTimestamped.StampCreated(DateTime utcNow) => CreatedAt = utcNow;
}
