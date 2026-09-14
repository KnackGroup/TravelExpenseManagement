using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;

namespace TravelExpense.Api.Services;

public record PolicyEvaluationResult(bool IsException, string? Reason, decimal? CapAmountApplied);

/// <summary>
/// The core of "dynamic management of hierarchy, expense heads, maximum cap, and whether
/// flights/trains are allowed": given an employee's role and one claimed expense line, look
/// up the currently-effective PolicyCap row for (role, category) and decide whether the
/// claim is within policy or must be flagged as an exception.
///
/// Per the business rule: an exception does NOT block submission - it is still saved, just
/// flagged, and routed for explicit approval by the reporting authority.
/// </summary>
public class PolicyEngine
{
    private readonly AppDbContext _db;

    public PolicyEngine(AppDbContext db) => _db = db;

    public async Task<PolicyCap?> GetEffectiveCapAsync(int roleId, int expenseCategoryId, DateOnly onDate)
    {
        return await _db.PolicyCaps
            .Where(p => p.RoleId == roleId
                        && p.ExpenseCategoryId == expenseCategoryId
                        && p.EffectiveFrom <= onDate
                        && (p.EffectiveTo == null || p.EffectiveTo >= onDate))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync();
    }

    public async Task<PolicyEvaluationResult> EvaluateAsync(
        int roleId, int expenseCategoryId, DateOnly expenseDate, decimal claimedAmount, ExpenseCategory category)
    {
        var cap = await GetEffectiveCapAsync(roleId, expenseCategoryId, expenseDate);

        if (cap == null)
        {
            // No policy configured at all for this role/category - treat conservatively as an exception
            // so it always gets a human decision rather than silently auto-passing.
            return new PolicyEvaluationResult(true, $"No policy configured for this role/category ({category.CategoryName}).", null);
        }

        if (!cap.IsAllowed)
        {
            return new PolicyEvaluationResult(true, $"{category.CategoryName} is not permitted for this role.", 0m);
        }

        // Ticket categories (flight/train) are checked against the per-booking cap; everything
        // else (food/hotel/local conveyance/misc) is checked against the per-day cap.
        var relevantCap = category.IsTicketCategory ? cap.MaxAmountPerBooking : cap.MaxAmountPerDay;

        if (relevantCap.HasValue && claimedAmount > relevantCap.Value)
        {
            var unit = category.IsTicketCategory ? "per-booking" : "per-day";
            return new PolicyEvaluationResult(
                true,
                $"Claimed {claimedAmount:0.00} exceeds the {unit} policy cap of {relevantCap.Value:0.00}{(string.IsNullOrEmpty(cap.MaxClass) ? "" : $" ({cap.MaxClass} class)")}.",
                relevantCap);
        }

        return new PolicyEvaluationResult(false, null, relevantCap);
    }
}
