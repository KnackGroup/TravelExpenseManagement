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
/// Advance request workflow: employee requests against a tour plan -> immediate reporting
/// manager (the "Superior" stage) approves -> Accounts approves and records the payout.
/// Every stage is resolved dynamically off Employee.ManagerEmployeeId and Role.RoleType, so
/// it automatically follows whatever hierarchy edits are made in the Admin screens.
/// </summary>
[ApiController]
[Route("api/advances")]
[Authorize]
public class AdvancesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ApprovalRoutingService _routing;
    private readonly NotificationService _notifications;

    public AdvancesController(AppDbContext db, ApprovalRoutingService routing, NotificationService notifications)
    {
        _db = db;
        _routing = routing;
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdvanceRequestDto>>> GetMine()
    {
        var employeeId = User.GetEmployeeId();
        var requests = await Query().Where(a => a.EmployeeId == employeeId).OrderByDescending(a => a.CreatedAt).ToListAsync();
        return Ok(requests.Select(ToDto));
    }

    /// <summary>Advance requests currently awaiting a decision from the caller: their direct
    /// reports' pending "Superior" stage requests, or - for Accounts users - every request
    /// that has cleared the Superior stage and is awaiting Accounts.</summary>
    [HttpGet("inbox")]
    public async Task<ActionResult<List<AdvanceRequestDto>>> GetInbox()
    {
        var employeeId = User.GetEmployeeId();
        var isAccounts = await _routing.IsAccountsUserAsync(employeeId);

        List<AdvanceRequest> requests;
        if (isAccounts)
        {
            requests = await Query().Where(a => a.Status == AdvanceRequestStatus.ApprovedBySuperior).ToListAsync();
        }
        else
        {
            requests = await Query()
                .Where(a => a.Status == AdvanceRequestStatus.Pending && a.Employee.ManagerEmployeeId == employeeId)
                .ToListAsync();
        }
        return Ok(requests.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<AdvanceRequestDto>> Create(CreateAdvanceRequest request)
    {
        var employeeId = User.GetEmployeeId();

        var plan = await _db.TourPlans.FirstOrDefaultAsync(t => t.TourPlanId == request.TourPlanId);
        if (plan == null) return NotFound(new { message = "Tour plan not found." });
        if (plan.EmployeeId != employeeId) return Forbid();

        var advance = new AdvanceRequest
        {
            TourPlanId = request.TourPlanId,
            EmployeeId = employeeId,
            RequestedAmount = request.RequestedAmount,
            RequestNotes = request.RequestNotes,
            Status = AdvanceRequestStatus.Pending
        };
        _db.AdvanceRequests.Add(advance);
        await _db.SaveChangesAsync();

        var saved = await Query().FirstAsync(a => a.AdvanceRequestId == advance.AdvanceRequestId);

        var manager = await _routing.GetSuperiorAsync(employeeId);
        if (manager != null)
        {
            await _notifications.NotifyAsync(
                NotificationEventType.AdvanceRequested,
                new[] { new NotificationRecipient(manager.EmployeeId, manager.Email, manager.FullName) },
                new Dictionary<string, string?>
                {
                    ["EmployeeName"] = saved.Employee.FullName,
                    ["TourPlanTitle"] = saved.TourPlan.Title,
                    ["Amount"] = saved.RequestedAmount.ToString("0.00"),
                    ["Currency"] = saved.Currency
                },
                relatedEntityType: "AdvanceRequest", relatedEntityId: saved.AdvanceRequestId);
        }

        return Ok(ToDto(saved));
    }

    [HttpPost("{advanceRequestId:int}/decide/superior")]
    public async Task<ActionResult<AdvanceRequestDto>> DecideSuperior(int advanceRequestId, AdvanceDecisionRequest request)
    {
        var approverId = User.GetEmployeeId();
        var advance = await _db.AdvanceRequests.Include(a => a.Employee).FirstOrDefaultAsync(a => a.AdvanceRequestId == advanceRequestId);
        if (advance == null) return NotFound();
        if (advance.Status != AdvanceRequestStatus.Pending) return BadRequest(new { message = "Request is not awaiting superior approval." });
        if (advance.Employee.ManagerEmployeeId != approverId) return Forbid();

        _db.AdvanceApprovals.Add(new AdvanceApproval
        {
            AdvanceRequestId = advanceRequestId,
            ApproverEmployeeId = approverId,
            ApprovalStage = ApprovalStage.Superior,
            Decision = request.Approve ? ApprovalDecision.Approved : ApprovalDecision.Rejected,
            ApprovedAmount = request.Approve ? (request.ApprovedAmount ?? advance.RequestedAmount) : null,
            Comments = request.Comments
        });

        advance.Status = request.Approve ? AdvanceRequestStatus.ApprovedBySuperior : AdvanceRequestStatus.RejectedBySuperior;
        advance.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var saved = await Query().FirstAsync(a => a.AdvanceRequestId == advanceRequestId);

        if (request.Approve)
        {
            var accountsUsers = await _routing.GetAccountsApproversAsync();
            await _notifications.NotifyAsync(
                NotificationEventType.AdvanceApprovedBySuperior,
                accountsUsers.Select(u => new NotificationRecipient(u.EmployeeId, u.Email, u.FullName)),
                new Dictionary<string, string?>
                {
                    ["EmployeeName"] = saved.Employee.FullName,
                    ["TourPlanTitle"] = saved.TourPlan.Title,
                    ["Amount"] = saved.RequestedAmount.ToString("0.00"),
                    ["Currency"] = saved.Currency
                },
                relatedEntityType: "AdvanceRequest", relatedEntityId: saved.AdvanceRequestId);
        }
        else
        {
            await _notifications.NotifyAsync(
                NotificationEventType.AdvanceRejected,
                new[] { new NotificationRecipient(saved.EmployeeId, saved.Employee.Email, saved.Employee.FullName) },
                new Dictionary<string, string?>
                {
                    ["TourPlanTitle"] = saved.TourPlan.Title,
                    ["Amount"] = saved.RequestedAmount.ToString("0.00"),
                    ["Currency"] = saved.Currency,
                    ["Comments"] = request.Comments
                },
                relatedEntityType: "AdvanceRequest", relatedEntityId: saved.AdvanceRequestId);
        }

        return Ok(ToDto(saved));
    }

    [HttpPost("{advanceRequestId:int}/decide/accounts")]
    [Authorize(Roles = RoleType.Accounts)]
    public async Task<ActionResult<AdvanceRequestDto>> DecideAccounts(int advanceRequestId, AdvanceDecisionRequest request)
    {
        var approverId = User.GetEmployeeId();
        var advance = await _db.AdvanceRequests.FirstOrDefaultAsync(a => a.AdvanceRequestId == advanceRequestId);
        if (advance == null) return NotFound();
        if (advance.Status != AdvanceRequestStatus.ApprovedBySuperior)
            return BadRequest(new { message = "Request has not been approved by the superior yet." });

        _db.AdvanceApprovals.Add(new AdvanceApproval
        {
            AdvanceRequestId = advanceRequestId,
            ApproverEmployeeId = approverId,
            ApprovalStage = ApprovalStage.Accounts,
            Decision = request.Approve ? ApprovalDecision.Approved : ApprovalDecision.Rejected,
            ApprovedAmount = request.Approve ? (request.ApprovedAmount ?? advance.RequestedAmount) : null,
            Comments = request.Comments
        });

        advance.Status = request.Approve ? AdvanceRequestStatus.ApprovedByAccounts : AdvanceRequestStatus.RejectedByAccounts;
        advance.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var saved = await Query().FirstAsync(a => a.AdvanceRequestId == advanceRequestId);

        if (!request.Approve)
        {
            await _notifications.NotifyAsync(
                NotificationEventType.AdvanceRejected,
                new[] { new NotificationRecipient(saved.EmployeeId, saved.Employee.Email, saved.Employee.FullName) },
                new Dictionary<string, string?>
                {
                    ["TourPlanTitle"] = saved.TourPlan.Title,
                    ["Amount"] = saved.RequestedAmount.ToString("0.00"),
                    ["Currency"] = saved.Currency,
                    ["Comments"] = request.Comments
                },
                relatedEntityType: "AdvanceRequest", relatedEntityId: saved.AdvanceRequestId);
        }

        return Ok(ToDto(saved));
    }

    [HttpPost("{advanceRequestId:int}/disburse")]
    [Authorize(Roles = RoleType.Accounts)]
    public async Task<ActionResult<AdvanceRequestDto>> Disburse(int advanceRequestId, DisburseAdvanceRequest request)
    {
        var approverId = User.GetEmployeeId();
        var advance = await _db.AdvanceRequests.FirstOrDefaultAsync(a => a.AdvanceRequestId == advanceRequestId);
        if (advance == null) return NotFound();
        if (advance.Status != AdvanceRequestStatus.ApprovedByAccounts)
            return BadRequest(new { message = "Request must be approved by Accounts before it can be disbursed." });

        _db.AdvanceDisbursements.Add(new AdvanceDisbursement
        {
            AdvanceRequestId = advanceRequestId,
            DisbursementMode = request.DisbursementMode,
            Amount = request.Amount,
            ReferenceNo = request.ReferenceNo,
            DisbursedByEmployeeId = approverId
        });

        advance.Status = AdvanceRequestStatus.Disbursed;
        advance.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var saved = await Query().FirstAsync(a => a.AdvanceRequestId == advanceRequestId);

        await _notifications.NotifyAsync(
            NotificationEventType.AdvanceDisbursed,
            new[] { new NotificationRecipient(saved.EmployeeId, saved.Employee.Email, saved.Employee.FullName) },
            new Dictionary<string, string?>
            {
                ["TourPlanTitle"] = saved.TourPlan.Title,
                ["Amount"] = request.Amount.ToString("0.00"),
                ["Currency"] = saved.Currency
            },
            relatedEntityType: "AdvanceRequest", relatedEntityId: saved.AdvanceRequestId);

        return Ok(ToDto(saved));
    }

    private IQueryable<AdvanceRequest> Query() => _db.AdvanceRequests
        .Include(a => a.TourPlan)
        .Include(a => a.Employee)
        .Include(a => a.Approvals).ThenInclude(ap => ap.Approver)
        .Include(a => a.Disbursement).ThenInclude(d => d!.DisbursedBy);

    private static AdvanceRequestDto ToDto(AdvanceRequest a) => new(
        a.AdvanceRequestId, a.TourPlanId, a.TourPlan.Title, a.EmployeeId, a.Employee.FullName,
        a.RequestedAmount, a.Currency, a.RequestNotes, a.Status, a.CreatedAt,
        a.Approvals.OrderBy(ap => ap.DecidedAt)
            .Select(ap => new AdvanceApprovalDto(ap.AdvanceApprovalId, ap.Approver.FullName, ap.ApprovalStage, ap.Decision, ap.ApprovedAmount, ap.Comments, ap.DecidedAt))
            .ToList(),
        a.Disbursement == null ? null : new AdvanceDisbursementDto(
            a.Disbursement.DisbursementMode, a.Disbursement.Amount, a.Disbursement.ReferenceNo,
            a.Disbursement.DisbursedBy.FullName, a.Disbursement.DisbursedAt));
}
