using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Auth;
using TravelExpense.Api.Data;
using TravelExpense.Api.Dtos;
using TravelExpense.Api.Services;

namespace TravelExpense.Api.Controllers;

/// <summary>
/// The accounting reconciliation view the requirement asked for: advance taken vs. expense
/// claimed vs. expense approved vs. tour outcome, per trip - backed by the
/// vw_TripFinancialSummary SQL view so the math (and any future audit) lives in one place.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ApprovalRoutingService _routing;

    public ReportsController(AppDbContext db, ApprovalRoutingService routing)
    {
        _db = db;
        _routing = routing;
    }

    /// <summary>Trip-by-trip financial reconciliation. Accounts/Admin see everyone; a manager
    /// sees their team's trips (direct + indirect); an individual contributor sees their own.</summary>
    [HttpGet("trip-summary")]
    public async Task<ActionResult<List<TripFinancialSummaryDto>>> GetTripSummary()
    {
        var employeeId = User.GetEmployeeId();
        var isAccounts = await _routing.IsAccountsUserAsync(employeeId);
        var isAdmin = await _routing.IsAdminUserAsync(employeeId);

        IQueryable<Data.Entities.TripFinancialSummaryRow> query = _db.TripFinancialSummary;

        if (!isAccounts && !isAdmin)
        {
            // Everyone in the caller's reporting tree (self + all descendants), computed via
            // the same ManagerEmployeeId chain used for approval routing.
            var teamIds = await GetSubtreeEmployeeIdsAsync(employeeId);
            query = query.Where(r => teamIds.Contains(r.EmployeeId));
        }

        var rows = await query.OrderByDescending(r => r.StartDate).ToListAsync();

        return Ok(rows.Select(r => new TripFinancialSummaryDto(
            r.TourPlanId, r.EmployeeId, r.EmployeeName, r.RoleName, r.ChannelName, r.Title,
            r.StartDate, r.EndDate, r.TourPlanStatus, r.TotalAdvanceDisbursed, r.TotalExpenseClaimed,
            r.TotalExpenseApproved, r.HasPolicyExceptions, r.SettlementAmount,
            r.DailyReportsSubmitted ?? 0, r.LastOutcomeSummary)));
    }

    [HttpGet("pending-exceptions")]
    public async Task<ActionResult<List<PendingPolicyExceptionDto>>> GetPendingExceptions()
    {
        var employeeId = User.GetEmployeeId();
        var isAccounts = await _routing.IsAccountsUserAsync(employeeId);
        var isAdmin = await _routing.IsAdminUserAsync(employeeId);

        IQueryable<Data.Entities.PendingPolicyExceptionRow> query = _db.PendingPolicyExceptions;

        if (!isAccounts && !isAdmin)
        {
            var directReportIds = await _db.Employees.Where(e => e.ManagerEmployeeId == employeeId).Select(e => e.EmployeeId).ToListAsync();
            query = query.Where(r => directReportIds.Contains(r.EmployeeId));
        }

        var rows = await query.ToListAsync();
        return Ok(rows.Select(r => new PendingPolicyExceptionDto(
            r.ExpenseLineItemId, r.ExpenseReportId, r.TourPlanId, r.EmployeeId, r.EmployeeName,
            r.CategoryName, r.ExpenseDate, r.ClaimedAmount, r.PolicyCapAmountApplied, r.ExceptionReason, r.LineStatus)));
    }

    private async Task<List<int>> GetSubtreeEmployeeIdsAsync(int rootEmployeeId)
    {
        var all = await _db.Employees.Select(e => new { e.EmployeeId, e.ManagerEmployeeId }).ToListAsync();
        var result = new List<int> { rootEmployeeId };
        var frontier = new List<int> { rootEmployeeId };

        while (frontier.Count > 0)
        {
            var next = all.Where(e => e.ManagerEmployeeId.HasValue && frontier.Contains(e.ManagerEmployeeId.Value))
                           .Select(e => e.EmployeeId).ToList();
            result.AddRange(next);
            frontier = next;
        }

        return result;
    }
}
