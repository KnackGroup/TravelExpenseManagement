using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;
using TravelExpense.Api.Dtos;

namespace TravelExpense.Api.Services.Ai;

/// <summary>
/// "AI-lite": decision support computed entirely from data already in this database - no
/// external model, no API key, works the moment the app is deployed. For a compliance
/// workflow like travel-expense approval, grounded/deterministic answers are arguably *more*
/// trustworthy than a generic LLM anyway (zero hallucination risk on "is this within policy").
///
/// This is what actually reduces approver and rep workload day to day: a risk score next to
/// every exception so managers don't have to dig through history themselves, a suggested
/// advance amount so reps don't have to guess, anomaly flags so nothing slips through before
/// month-end audit, and an instant, always-correct answer to "am I allowed to book a flight".
/// </summary>
public class AiInsightService
{
    private readonly AppDbContext _db;

    public AiInsightService(AppDbContext db) => _db = db;

    public async Task<ExceptionRiskAssessmentDto?> AssessExceptionRiskAsync(int lineItemId)
    {
        var line = await _db.ExpenseLineItems
            .Include(li => li.ExpenseReport).ThenInclude(r => r.Employee)
            .Include(li => li.ExpenseCategory)
            .FirstOrDefaultAsync(li => li.ExpenseLineItemId == lineItemId);

        if (line == null) return null;

        var employeeId = line.ExpenseReport.EmployeeId;
        var since = DateTime.UtcNow.AddDays(-90);

        var recentExceptions = await _db.ExpenseLineItems
            .Where(li => li.ExpenseReport.EmployeeId == employeeId && li.IsPolicyException && li.CreatedAt >= since)
            .ToListAsync();

        var decidedRecent = recentExceptions.Where(li => li.LineStatus != LineStatus.Pending).ToList();
        var approvalRate = decidedRecent.Count == 0
            ? (decimal?)null
            : Math.Round(100m * decidedRecent.Count(li => li.LineStatus == LineStatus.Approved) / decidedRecent.Count, 0);

        // Hard "not allowed at all" case (PolicyEngine records CapAmountApplied = 0 for these).
        if (line.PolicyCapAmountApplied == 0m)
        {
            return new ExceptionRiskAssessmentDto(
                lineItemId, "High",
                "Needs explicit justification",
                $"{line.ExpenseCategory.CategoryName} is not permitted at all for this role - this isn't a borderline overage, it's a disallowed category.",
                null, recentExceptions.Count, approvalRate);
        }

        decimal? overagePercent = line.PolicyCapAmountApplied is > 0
            ? Math.Round(100m * (line.ClaimedAmount - line.PolicyCapAmountApplied.Value) / line.PolicyCapAmountApplied.Value, 0)
            : null;

        var riskScore = 0;
        if (overagePercent is > 30) riskScore += 2;
        else if (overagePercent is > 10) riskScore += 1;
        if (recentExceptions.Count > 3) riskScore += 1;
        if (approvalRate is < 50) riskScore += 1;

        var riskLevel = riskScore >= 3 ? "High" : riskScore >= 1 ? "Medium" : "Low";
        var recommendation = riskLevel switch
        {
            "Low" => "Likely safe to approve",
            "Medium" => "Review before deciding",
            _ => "Review closely / consider rejecting or partial approval"
        };

        var rationaleParts = new List<string>();
        if (overagePercent.HasValue) rationaleParts.Add($"{overagePercent}% over the {(line.ExpenseCategory.IsTicketCategory ? "per-booking" : "per-day")} cap");
        rationaleParts.Add($"{recentExceptions.Count} exception(s) from this employee in the last 90 days");
        if (approvalRate.HasValue) rationaleParts.Add($"{approvalRate}% of their recent exceptions were approved");

        return new ExceptionRiskAssessmentDto(
            lineItemId, riskLevel, recommendation, string.Join("; ", rationaleParts) + ".",
            overagePercent, recentExceptions.Count, approvalRate);
    }

    public async Task<AdvanceSuggestionDto> SuggestAdvanceAmountAsync(int tourPlanId)
    {
        var plan = await _db.TourPlans.Include(t => t.Employee).FirstOrDefaultAsync(t => t.TourPlanId == tourPlanId);
        if (plan == null) throw new KeyNotFoundException("Tour plan not found.");

        var durationDays = plan.EndDate.DayNumber - plan.StartDate.DayNumber + 1;
        if (durationDays < 1) durationDays = 1;

        // Prefer the employee's own history: average approved-expense-per-day across their
        // past completed trips (same channel), applied to this trip's length.
        var pastTripsRaw = await _db.TourPlans
            .Where(t => t.EmployeeId == plan.EmployeeId && t.TourPlanId != tourPlanId && t.ChannelId == plan.ChannelId)
            .Select(t => new
            {
                t.TourPlanId,
                t.StartDate,
                t.EndDate,
                ApprovedTotal = t.ExpenseReports.Sum(r => (decimal?)r.TotalApprovedAmount) ?? 0m
            })
            .Where(t => t.ApprovedTotal > 0)
            .ToListAsync();

        // DurationDays computed client-side (DateOnly.DayNumber has no reliable SQL Server
        // translation) after the rows are already materialized.
        var pastTrips = pastTripsRaw
            .Select(t => new { t.TourPlanId, ApprovedTotal = t.ApprovedTotal, DurationDays = Math.Max(t.EndDate.DayNumber - t.StartDate.DayNumber + 1, 1) })
            .ToList();

        if (pastTrips.Count > 0)
        {
            var avgPerDay = pastTrips.Average(t => t.ApprovedTotal / t.DurationDays);
            var suggestion = RoundToNearestHundred(avgPerDay * durationDays * 1.1m); // +10% buffer, rounded to nearest 100
            return new AdvanceSuggestionDto(tourPlanId, suggestion, "INR",
                $"Based on your average approved spend/day across {pastTrips.Count} past trip(s) on this channel, plus a 10% buffer.",
                pastTrips.Count);
        }

        // No history yet - fall back to the role's policy caps (food + hotel + local + misc;
        // tickets are typically booked/claimed separately so excluded from the daily estimate).
        var dailyCategoryCodes = new[] { "FOOD", "HOTEL", "LOCAL", "MISC" };
        var onDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var caps = await _db.PolicyCaps
            .Include(p => p.ExpenseCategory)
            .Where(p => p.RoleId == plan.Employee.RoleId && dailyCategoryCodes.Contains(p.ExpenseCategory.CategoryCode)
                        && p.IsAllowed && p.EffectiveFrom <= onDate && (p.EffectiveTo == null || p.EffectiveTo >= onDate))
            .ToListAsync();

        var dailyCapTotal = caps.Sum(c => c.MaxAmountPerDay ?? 0m);
        var fallbackSuggestion = RoundToNearestHundred(dailyCapTotal * durationDays);

        return new AdvanceSuggestionDto(tourPlanId, fallbackSuggestion, "INR",
            "Based on your role's daily policy caps (no prior trip history yet to average from).", 0);
    }

    public async Task<List<AnomalyFlagDto>> DetectAnomaliesAsync(int expenseReportId)
    {
        var report = await _db.ExpenseReports
            .Include(r => r.TourPlan)
            .Include(r => r.LineItems).ThenInclude(li => li.ExpenseCategory)
            .FirstOrDefaultAsync(r => r.ExpenseReportId == expenseReportId);

        if (report == null) return new List<AnomalyFlagDto>();

        var flags = new List<AnomalyFlagDto>();

        // 1. Duplicate-looking entries: same date + category + amount within the same report.
        var duplicateGroups = report.LineItems
            .GroupBy(li => (li.ExpenseDate, li.ExpenseCategoryId, li.ClaimedAmount))
            .Where(g => g.Count() > 1);
        foreach (var group in duplicateGroups)
            foreach (var li in group)
                flags.Add(new AnomalyFlagDto(li.ExpenseLineItemId, "Duplicate",
                    "Medium", $"Another line item with the same date, category, and amount ({li.ClaimedAmount:0.00}) appears in this report - possible duplicate entry."));

        // 2. Dates outside the approved tour plan window.
        foreach (var li in report.LineItems)
        {
            if (li.ExpenseDate < report.TourPlan.StartDate || li.ExpenseDate > report.TourPlan.EndDate)
                flags.Add(new AnomalyFlagDto(li.ExpenseLineItemId, "DateOutsideTrip",
                    "Medium", $"Expense date {li.ExpenseDate} falls outside the approved tour plan window ({report.TourPlan.StartDate} to {report.TourPlan.EndDate})."));
        }

        // 3. Suspiciously round numbers on categories that are rarely exactly round (food/local).
        foreach (var li in report.LineItems)
        {
            if ((li.ExpenseCategory.CategoryCode is "FOOD" or "LOCAL") && li.ClaimedAmount >= 500 && li.ClaimedAmount % 500 == 0)
                flags.Add(new AnomalyFlagDto(li.ExpenseLineItemId, "RoundNumber",
                    "Low", $"{li.ClaimedAmount:0.00} is a suspiciously round figure for {li.ExpenseCategory.CategoryName} - worth confirming a receipt exists."));
        }

        // 4. Spend spikes vs. this employee's own history in the same category.
        var employeeId = report.EmployeeId;
        foreach (var categoryGroup in report.LineItems.GroupBy(li => li.ExpenseCategoryId))
        {
            var categoryId = categoryGroup.Key;
            var historicalAvg = await _db.ExpenseLineItems
                .Where(li => li.ExpenseReport.EmployeeId == employeeId && li.ExpenseCategoryId == categoryId && li.ExpenseReportId != expenseReportId)
                .Select(li => (decimal?)li.ClaimedAmount)
                .AverageAsync();

            if (historicalAvg is > 0)
            {
                foreach (var li in categoryGroup.Where(li => li.ClaimedAmount > historicalAvg.Value * 2))
                {
                    flags.Add(new AnomalyFlagDto(li.ExpenseLineItemId, "SpendSpike", "Low",
                        $"{li.ClaimedAmount:0.00} is more than double this employee's historical average of {historicalAvg.Value:0.00} for {li.ExpenseCategory.CategoryName}."));
                }
            }
        }

        return flags;
    }

    public async Task<PolicyAnswerDto> AnswerPolicyQuestionAsync(string question, int roleId)
    {
        var role = await _db.Roles.Include(r => r.Channel).FirstOrDefaultAsync(r => r.RoleId == roleId);
        if (role == null) return new PolicyAnswerDto("I don't recognize that role.", false);

        var categories = await _db.ExpenseCategories.Where(c => c.IsActive).ToListAsync();
        var lowerQuestion = question.ToLowerInvariant();

        var matchedCategory = categories.FirstOrDefault(c =>
            lowerQuestion.Contains(c.CategoryName.ToLowerInvariant().Split(' ')[0]) ||
            lowerQuestion.Contains(c.CategoryCode.ToLowerInvariant()) ||
            (c.CategoryCode == "LOCAL" && (lowerQuestion.Contains("cab") || lowerQuestion.Contains("taxi") || lowerQuestion.Contains("conveyance"))) ||
            (c.CategoryCode == "FLIGHT" && lowerQuestion.Contains("fly")) ||
            (c.CategoryCode == "HOTEL" && (lowerQuestion.Contains("stay") || lowerQuestion.Contains("accommodation"))));

        var roleLabel = role.RoleName + (role.Channel != null ? $" ({role.Channel.ChannelName})" : "");

        if (matchedCategory == null)
        {
            var list = string.Join(", ", categories.Select(c => c.CategoryName));
            return new PolicyAnswerDto(
                $"I can answer questions about your travel policy for {roleLabel} across these categories: {list}. " +
                "Try asking something like \"Can I book a flight?\" or \"What's my hotel cap?\".", true);
        }

        var onDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var cap = await _db.PolicyCaps
            .Where(p => p.RoleId == roleId && p.ExpenseCategoryId == matchedCategory.ExpenseCategoryId
                        && p.EffectiveFrom <= onDate && (p.EffectiveTo == null || p.EffectiveTo >= onDate))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync();

        if (cap == null)
            return new PolicyAnswerDto($"There's no policy configured yet for {matchedCategory.CategoryName} for {roleLabel} - check with your admin.", true);

        if (!cap.IsAllowed)
            return new PolicyAnswerDto($"No - {matchedCategory.CategoryName} is not permitted for {roleLabel}.", true);

        var capText = matchedCategory.IsTicketCategory
            ? (cap.MaxAmountPerBooking.HasValue ? $"up to {cap.Currency} {cap.MaxAmountPerBooking:0.00} per booking{(cap.MaxClass != null ? $" ({cap.MaxClass} class)" : "")}" : "with no fixed per-booking cap")
            : (cap.MaxAmountPerDay.HasValue ? $"up to {cap.Currency} {cap.MaxAmountPerDay:0.00} per day{(cap.MaxClass != null ? $" ({cap.MaxClass})" : "")}" : "with no fixed per-day cap");

        return new PolicyAnswerDto($"Yes - {matchedCategory.CategoryName} is allowed for {roleLabel}, {capText}.", true);
    }

    // Math.Round(decimal, int) only accepts 0-28 decimal places - it has no built-in support
    // for rounding to the nearest 100 (a negative "digits" value throws
    // ArgumentOutOfRangeException), so that's done manually here.
    private static decimal RoundToNearestHundred(decimal value) =>
        Math.Round(value / 100m, 0, MidpointRounding.AwayFromZero) * 100m;
}
