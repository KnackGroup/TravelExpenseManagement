namespace TravelExpense.Api.Data.Entities;

public static class AdvanceRequestStatus
{
    public const string Pending = "Pending";
    public const string ApprovedBySuperior = "ApprovedBySuperior";
    public const string RejectedBySuperior = "RejectedBySuperior";
    public const string ApprovedByAccounts = "ApprovedByAccounts";
    public const string RejectedByAccounts = "RejectedByAccounts";
    public const string Disbursed = "Disbursed";
}

public static class ApprovalStage
{
    public const string Superior = "Superior";
    public const string Accounts = "Accounts";
}

public static class ApprovalDecision
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public class AdvanceRequest : ICreationTimestamped, IUpdateTimestamped
{
    public int AdvanceRequestId { get; set; }
    public int TourPlanId { get; set; }
    public int EmployeeId { get; set; }
    public decimal RequestedAmount { get; set; }
    public string Currency { get; set; } = "INR";
    public string? RequestNotes { get; set; }
    public string Status { get; set; } = AdvanceRequestStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public TourPlan TourPlan { get; set; } = default!;
    public Employee Employee { get; set; } = default!;
    public ICollection<AdvanceApproval> Approvals { get; set; } = new List<AdvanceApproval>();
    public AdvanceDisbursement? Disbursement { get; set; }

    void ICreationTimestamped.StampCreated(DateTime utcNow) => CreatedAt = utcNow;
    void IUpdateTimestamped.StampUpdated(DateTime utcNow) => UpdatedAt = utcNow;
}

public class AdvanceApproval : ICreationTimestamped
{
    public int AdvanceApprovalId { get; set; }
    public int AdvanceRequestId { get; set; }
    public int ApproverEmployeeId { get; set; }
    public string ApprovalStage { get; set; } = default!;
    public string Decision { get; set; } = default!;
    public decimal? ApprovedAmount { get; set; }
    public string? Comments { get; set; }
    public DateTime DecidedAt { get; set; }

    public AdvanceRequest AdvanceRequest { get; set; } = default!;
    public Employee Approver { get; set; } = default!;

    // DecidedAt is the "when was this decision made" business timestamp, which is also
    // exactly the record's creation time (an approval row is written once and never
    // updated), so it's stamped through the same creation hook as CreatedAt elsewhere.
    void ICreationTimestamped.StampCreated(DateTime utcNow) => DecidedAt = utcNow;
}

public class AdvanceDisbursement : ICreationTimestamped
{
    public int AdvanceDisbursementId { get; set; }
    public int AdvanceRequestId { get; set; }
    public string DisbursementMode { get; set; } = default!; // Cash | BankTransfer
    public decimal Amount { get; set; }
    public string? ReferenceNo { get; set; }
    public int DisbursedByEmployeeId { get; set; }
    public DateTime DisbursedAt { get; set; }

    public AdvanceRequest AdvanceRequest { get; set; } = default!;
    public Employee DisbursedBy { get; set; } = default!;

    void ICreationTimestamped.StampCreated(DateTime utcNow) => DisbursedAt = utcNow;
}
