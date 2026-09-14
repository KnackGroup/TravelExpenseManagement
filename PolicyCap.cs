namespace TravelExpense.Api.Data.Entities;

/// <summary>
/// One row = "for this Role, this ExpenseCategory is (or isn't) allowed, with these caps,
/// effective for this date range." This single table is the entire configurable travel
/// policy - per-diem caps, per-booking ticket caps, and flight/train eligibility.
/// </summary>
public class PolicyCap : ICreationTimestamped, IUpdateTimestamped
{
    public int PolicyCapId { get; set; }
    public int RoleId { get; set; }
    public int ExpenseCategoryId { get; set; }
    public bool IsAllowed { get; set; } = true;
    public decimal? MaxAmountPerDay { get; set; }
    public decimal? MaxAmountPerBooking { get; set; }
    public string? MaxClass { get; set; }
    public string Currency { get; set; } = "INR";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int? CreatedByEmployeeId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Role Role { get; set; } = default!;
    public ExpenseCategory ExpenseCategory { get; set; } = default!;

    void ICreationTimestamped.StampCreated(DateTime utcNow) => CreatedAt = utcNow;
    void IUpdateTimestamped.StampUpdated(DateTime utcNow) => UpdatedAt = utcNow;
}
