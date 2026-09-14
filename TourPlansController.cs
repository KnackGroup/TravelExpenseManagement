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

[ApiController]
[Route("api/tour-plans")]
[Authorize]
public class TourPlansController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ApprovalRoutingService _routing;
    private readonly NotificationService _notifications;

    public TourPlansController(AppDbContext db, ApprovalRoutingService routing, NotificationService notifications)
    {
        _db = db;
        _routing = routing;
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<ActionResult<List<TourPlanDto>>> GetMine([FromQuery] int? employeeId)
    {
        var meId = User.GetEmployeeId();
        var targetId = employeeId ?? meId;

        // Anyone can list their own. Viewing someone else's requires being Accounts/Admin, or
        // being somewhere above them in the reporting chain (their manager, their manager's
        // manager, etc.) - the frontend uses this for approval/oversight views.
        if (targetId != meId)
        {
            var isPrivileged = await _routing.IsAccountsUserAsync(meId) || await _routing.IsAdminUserAsync(meId);
            if (!isPrivileged && !await _routing.IsInReportingChainAsync(targetId, meId))
                return Forbid();
        }

        var plans = await _db.TourPlans
            .Include(t => t.Employee)
            .Include(t => t.Channel)
            .Include(t => t.Stops)
            .Where(t => t.EmployeeId == targetId)
            .OrderByDescending(t => t.StartDate)
            .ToListAsync();

        return Ok(plans.Select(ToDto));
    }

    [HttpGet("{tourPlanId:int}")]
    public async Task<ActionResult<TourPlanDto>> GetById(int tourPlanId)
    {
        var plan = await _db.TourPlans
            .Include(t => t.Employee)
            .Include(t => t.Channel)
            .Include(t => t.Stops)
            .FirstOrDefaultAsync(t => t.TourPlanId == tourPlanId);

        if (plan == null) return NotFound();

        var meId = User.GetEmployeeId();
        if (plan.EmployeeId != meId)
        {
            var isPrivileged = await _routing.IsAccountsUserAsync(meId) || await _routing.IsAdminUserAsync(meId);
            if (!isPrivileged && !await _routing.IsInReportingChainAsync(plan.EmployeeId, meId))
                return Forbid();
        }

        return Ok(ToDto(plan));
    }

    [HttpPost]
    public async Task<ActionResult<TourPlanDto>> Create(CreateTourPlanRequest request)
    {
        var employeeId = User.GetEmployeeId();

        if (request.EndDate < request.StartDate)
            return BadRequest(new { message = "End date cannot be before start date." });

        var plan = new TourPlan
        {
            EmployeeId = employeeId,
            ChannelId = request.ChannelId,
            Title = request.Title,
            PurposeOfVisit = request.PurposeOfVisit,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = TourPlanStatus.Submitted,
            Stops = request.Stops.Select(s => new TourPlanStop
            {
                VisitDate = s.VisitDate,
                Location = s.Location,
                StateOrCountry = s.StateOrCountry,
                PurposeNotes = s.PurposeNotes
            }).ToList()
        };

        _db.TourPlans.Add(plan);
        await _db.SaveChangesAsync();

        await _db.Entry(plan).Reference(p => p.Employee).LoadAsync();
        await _db.Entry(plan).Reference(p => p.Channel).LoadAsync();

        var manager = await _routing.GetSuperiorAsync(employeeId);
        if (manager != null)
        {
            await _notifications.NotifyAsync(
                NotificationEventType.TourPlanSubmitted,
                new[] { new NotificationRecipient(manager.EmployeeId, manager.Email, manager.FullName) },
                new Dictionary<string, string?>
                {
                    ["EmployeeName"] = plan.Employee.FullName,
                    ["TourPlanTitle"] = plan.Title,
                    ["ChannelName"] = plan.Channel.ChannelName
                },
                relatedEntityType: "TourPlan", relatedEntityId: plan.TourPlanId);
        }

        return Ok(ToDto(plan));
    }

    private static TourPlanDto ToDto(TourPlan plan) => new(
        plan.TourPlanId, plan.EmployeeId, plan.Employee.FullName, plan.ChannelId, plan.Channel.ChannelName,
        plan.Title, plan.PurposeOfVisit, plan.StartDate, plan.EndDate, plan.Status,
        plan.Stops.Select(s => new TourPlanStopRequest(s.VisitDate, s.Location, s.StateOrCountry, s.PurposeNotes)).ToList(),
        plan.CreatedAt);
}
