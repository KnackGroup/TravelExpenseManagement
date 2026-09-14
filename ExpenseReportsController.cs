using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Auth;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;
using TravelExpense.Api.Dtos;
using TravelExpense.Api.Services;
using TravelExpense.Api.Services.Notifications;

namespace TravelExpense.Api.Controllers;

/// <summary>
/// Expense report submission with live policy-variance checking. Every line item is checked
/// against the submitting employee's role policy (PolicyEngine); items within policy are
/// auto-approved on submit, items over the cap (or for a disallowed category, e.g. a flight
/// claim from an Area Manager) are flagged as exceptions and routed to the employee's
/// reporting manager for an explicit approve/reject decision - the report is never blocked
/// from being submitted.
/// </summary>
[ApiController]
[Route("api/expense-reports")]
[Authorize]
public class ExpenseReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PolicyEngine _policyEngine;
    private readonly ApprovalRoutingService _routing;
    private readonly NotificationService _notifications;

    public ExpenseReportsController(AppDbContext db, PolicyEngine policyEngine, ApprovalRoutingService routing, NotificationService notifications)
    {
        _db = db;
        _policyEngine = policyEngine;
        _routing = routing;
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<ActionResult<List<ExpenseReportDto>>> GetMine()
    {
        var employeeId = User.GetEmployeeId();
        var reports = await Query().Where(r => r.EmployeeId == employeeId).OrderByDescending(r => r.CreatedAt).ToListAsync();
        return Ok(reports.Select(ToDto));
    }

    [HttpGet("{expenseReportId:int}")]
    public async Task<ActionResult<ExpenseReportDto>> GetById(int expenseReportId)
    {
        var report = await Query().FirstOrDefaultAsync(r => r.ExpenseReportId == expenseReportId);
        if (report == null) return NotFound();

        var meId = User.GetEmployeeId();
        if (report.EmployeeId != meId)
        {
            var isPrivileged = await _routing.IsAccountsUserAsync(meId) || await _routing.IsAdminUserAsync(meId);
            if (!isPrivileged && !await _routing.IsInReportingChainAsync(report.EmployeeId, meId))
                return Forbid();
        }

        return Ok(ToDto(report));
    }

    /// <summary>Reports containing at least one pending policy exception for one of the
    /// caller's direct reports - the reporting authority's action inbox.</summary>
    [HttpGet("inbox")]
    public async Task<ActionResult<List<ExpenseReportDto>>> GetInbox()
    {
        var managerId = User.GetEmployeeId();
        var reports = await Query()
            .Where(r => r.Employee.ManagerEmployeeId == managerId && r.LineItems.Any(li => li.IsPolicyException && li.LineStatus == LineStatus.Pending))
            .ToListAsync();
        return Ok(reports.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<ExpenseReportDto>> Create(CreateExpenseReportRequest request)
    {
        var employeeId = User.GetEmployeeId();

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
        if (employee == null) return Unauthorized();

        var plan = await _db.TourPlans.FirstOrDefaultAsync(t => t.TourPlanId == request.TourPlanId);
        if (plan == null) return NotFound(new { message = "Tour plan not found." });
        if (plan.EmployeeId != employeeId) return Forbid();

        if (request.LineItems.Count == 0)
            return BadRequest(new { message = "At least one expense line item is required." });

        var categoryIds = request.LineItems.Select(li => li.ExpenseCategoryId).Distinct().ToList();
        var categories = await _db.ExpenseCategories.Where(c => categoryIds.Contains(c.ExpenseCategoryId)).ToDictionaryAsync(c => c.ExpenseCategoryId);

        var report = new ExpenseReport
        {
            TourPlanId = request.TourPlanId,
            EmployeeId = employeeId,
            SubmittedAt = DateTime.UtcNow,
            Status = ExpenseReportStatus.Submitted
        };

        decimal totalClaimed = 0m;
        decimal totalApproved = 0m;
        var hasExceptions = false;

        foreach (var li in request.LineItems)
        {
            if (!categories.TryGetValue(li.ExpenseCategoryId, out var category))
                return BadRequest(new { message = $"Unknown expense category {li.ExpenseCategoryId}." });

            var evaluation = await _policyEngine.EvaluateAsync(employee.RoleId, li.ExpenseCategoryId, li.ExpenseDate, li.ClaimedAmount, category);
            totalClaimed += li.ClaimedAmount;

            var lineItem = new ExpenseLineItem
            {
                ExpenseDate = li.ExpenseDate,
                ExpenseCategoryId = li.ExpenseCategoryId,
                Description = li.Description,
                ClaimedAmount = li.ClaimedAmount,
                Currency = li.Currency,
                PolicyCapAmountApplied = evaluation.CapAmountApplied,
                IsPolicyException = evaluation.IsException,
                ExceptionReason = evaluation.Reason
            };

            if (evaluation.IsException)
            {
                hasExceptions = true;
                lineItem.LineStatus = LineStatus.Pending; // awaits reporting authority's decision
            }
            else
            {
                lineItem.LineStatus = LineStatus.Approved; // within policy - no discretion needed
                lineItem.ApprovedAmount = li.ClaimedAmount;
                lineItem.DecidedAt = DateTime.UtcNow;
                totalApproved += li.ClaimedAmount;
            }

            report.LineItems.Add(lineItem);
        }

        report.TotalClaimedAmount = totalClaimed;
        report.TotalApprovedAmount = totalApproved;
        report.HasPolicyExceptions = hasExceptions;
        report.Status = hasExceptions ? ExpenseReportStatus.UnderReview : ExpenseReportStatus.Approved;

        _db.ExpenseReports.Add(report);
        await _db.SaveChangesAsync();

        var saved = await Query().FirstAsync(r => r.ExpenseReportId == report.ExpenseReportId);

        await _notifications.NotifyAsync(
            NotificationEventType.ExpenseReportSubmitted,
            new[] { new NotificationRecipient(saved.EmployeeId, saved.Employee.Email, saved.Employee.FullName) },
            new Dictionary<string, string?>
            {
                ["TourPlanTitle"] = saved.TourPlan.Title,
                ["Amount"] = saved.TotalClaimedAmount.ToString("0.00"),
                ["Currency"] = "INR",
                ["Status"] = saved.Status
            },
            relatedEntityType: "ExpenseReport", relatedEntityId: saved.ExpenseReportId);

        if (hasExceptions)
        {
            var manager = await _routing.GetSuperiorAsync(employeeId);
            if (manager != null)
            {
                await _notifications.NotifyAsync(
                    NotificationEventType.ExpensePolicyExceptionRaised,
                    new[] { new NotificationRecipient(manager.EmployeeId, manager.Email, manager.FullName) },
                    new Dictionary<string, string?>
                    {
                        ["EmployeeName"] = saved.Employee.FullName,
                        ["TourPlanTitle"] = saved.TourPlan.Title
                    },
                    relatedEntityType: "ExpenseReport", relatedEntityId: saved.ExpenseReportId);
            }
        }

        return Ok(ToDto(saved));
    }

    /// <summary>Reporting authority decides on one or more pending exception (or any pending)
    /// line items in a single call. Recomputes the report's overall status and approved total.</summary>
    [HttpPost("{expenseReportId:int}/decide")]
    public async Task<ActionResult<ExpenseReportDto>> Decide(int expenseReportId, ExpenseReportDecisionRequest request)
    {
        var approverId = User.GetEmployeeId();
        var report = await _db.ExpenseReports
            .Include(r => r.Employee)
            .Include(r => r.LineItems)
            .FirstOrDefaultAsync(r => r.ExpenseReportId == expenseReportId);

        if (report == null) return NotFound();
        if (report.Employee.ManagerEmployeeId != approverId)
            return Forbid();

        foreach (var decision in request.Decisions)
        {
            var line = report.LineItems.FirstOrDefault(li => li.ExpenseLineItemId == decision.ExpenseLineItemId);
            if (line == null) continue;

            line.LineStatus = decision.Approve ? LineStatus.Approved : LineStatus.Rejected;
            line.ApprovedAmount = decision.Approve ? (decision.ApprovedAmount ?? line.ClaimedAmount) : 0m;
            line.ApproverEmployeeId = approverId;
            line.ApproverComments = decision.Comments;
            line.DecidedAt = DateTime.UtcNow;
        }

        var anyPending = report.LineItems.Any(li => li.LineStatus == LineStatus.Pending);
        var anyApproved = report.LineItems.Any(li => li.LineStatus == LineStatus.Approved);
        var anyRejected = report.LineItems.Any(li => li.LineStatus == LineStatus.Rejected);

        report.TotalApprovedAmount = report.LineItems.Where(li => li.LineStatus == LineStatus.Approved).Sum(li => li.ApprovedAmount ?? 0m);
        report.Status = anyPending
            ? ExpenseReportStatus.UnderReview
            : (anyApproved && anyRejected ? ExpenseReportStatus.PartiallyApproved
                : anyRejected ? ExpenseReportStatus.Rejected : ExpenseReportStatus.Approved);
        report.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var saved = await Query().FirstAsync(r => r.ExpenseReportId == expenseReportId);

        if (saved.Status != ExpenseReportStatus.UnderReview)
        {
            await _notifications.NotifyAsync(
                NotificationEventType.ExpenseReportDecided,
                new[] { new NotificationRecipient(saved.EmployeeId, saved.Employee.Email, saved.Employee.FullName) },
                new Dictionary<string, string?>
                {
                    ["TourPlanTitle"] = saved.TourPlan.Title,
                    ["Status"] = saved.Status,
                    ["Amount"] = (saved.TotalApprovedAmount ?? 0m).ToString("0.00"),
                    ["Currency"] = "INR"
                },
                relatedEntityType: "ExpenseReport", relatedEntityId: saved.ExpenseReportId);
        }

        return Ok(ToDto(saved));
    }

    private IQueryable<ExpenseReport> Query() => _db.ExpenseReports
        .Include(r => r.TourPlan)
        .Include(r => r.Employee)
        .Include(r => r.LineItems).ThenInclude(li => li.ExpenseCategory);

    private static ExpenseReportDto ToDto(ExpenseReport r) => new(
        r.ExpenseReportId, r.TourPlanId, r.TourPlan.Title, r.EmployeeId, r.Employee.FullName,
        r.SubmittedAt, r.Status, r.TotalClaimedAmount, r.TotalApprovedAmount, r.HasPolicyExceptions,
        r.LineItems.OrderBy(li => li.ExpenseDate).Select(li => new ExpenseLineItemDto(
            li.ExpenseLineItemId, li.ExpenseDate, li.ExpenseCategoryId, li.ExpenseCategory.CategoryName,
            li.Description, li.ClaimedAmount, li.Currency, li.PolicyCapAmountApplied,
            li.IsPolicyException, li.ExceptionReason, li.LineStatus, li.ApprovedAmount, li.ApproverComments)).ToList());
}
