using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Auth;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;
using TravelExpense.Api.Dtos;
using TravelExpense.Api.Services;

namespace TravelExpense.Api.Controllers;

/// <summary>Daily field summary submitted each day of a trip - work done, outcome, next-day
/// plan. One row per (TourPlan, ReportDate), visible to the employee and their manager.</summary>
[ApiController]
[Route("api/daily-reports")]
[Authorize]
public class DailyReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ApprovalRoutingService _routing;

    public DailyReportsController(AppDbContext db, ApprovalRoutingService routing)
    {
        _db = db;
        _routing = routing;
    }

    [HttpGet("by-tour-plan/{tourPlanId:int}")]
    public async Task<ActionResult<List<DailyReportDto>>> GetByTourPlan(int tourPlanId)
    {
        var plan = await _db.TourPlans.FirstOrDefaultAsync(t => t.TourPlanId == tourPlanId);
        if (plan == null) return NotFound();

        var meId = User.GetEmployeeId();
        if (plan.EmployeeId != meId)
        {
            var isPrivileged = await _routing.IsAccountsUserAsync(meId) || await _routing.IsAdminUserAsync(meId);
            if (!isPrivileged && !await _routing.IsInReportingChainAsync(plan.EmployeeId, meId))
                return Forbid();
        }

        var reports = await _db.DailyTourReports
            .Include(d => d.Employee)
            .Where(d => d.TourPlanId == tourPlanId)
            .OrderBy(d => d.ReportDate)
            .Select(d => new DailyReportDto(d.DailyTourReportId, d.TourPlanId, d.EmployeeId, d.Employee.FullName,
                d.ReportDate, d.VisitedLocations, d.WorkSummary, d.OutcomeSummary, d.NextDayPlan, d.CreatedAt))
            .ToListAsync();
        return Ok(reports);
    }

    [HttpPost]
    public async Task<ActionResult<DailyReportDto>> Create(CreateDailyReportRequest request)
    {
        var employeeId = User.GetEmployeeId();

        var plan = await _db.TourPlans.FirstOrDefaultAsync(t => t.TourPlanId == request.TourPlanId);
        if (plan == null) return NotFound(new { message = "Tour plan not found." });
        if (plan.EmployeeId != employeeId) return Forbid();

        var existing = await _db.DailyTourReports.FirstOrDefaultAsync(d => d.TourPlanId == request.TourPlanId && d.ReportDate == request.ReportDate);
        if (existing != null)
        {
            existing.VisitedLocations = request.VisitedLocations;
            existing.WorkSummary = request.WorkSummary;
            existing.OutcomeSummary = request.OutcomeSummary;
            existing.NextDayPlan = request.NextDayPlan;
            await _db.SaveChangesAsync();

            var emp = await _db.Employees.FindAsync(employeeId);
            return Ok(new DailyReportDto(existing.DailyTourReportId, existing.TourPlanId, existing.EmployeeId, emp!.FullName,
                existing.ReportDate, existing.VisitedLocations, existing.WorkSummary, existing.OutcomeSummary, existing.NextDayPlan, existing.CreatedAt));
        }

        var report = new DailyTourReport
        {
            TourPlanId = request.TourPlanId,
            EmployeeId = employeeId,
            ReportDate = request.ReportDate,
            VisitedLocations = request.VisitedLocations,
            WorkSummary = request.WorkSummary,
            OutcomeSummary = request.OutcomeSummary,
            NextDayPlan = request.NextDayPlan
        };
        _db.DailyTourReports.Add(report);
        await _db.SaveChangesAsync();

        var employee = await _db.Employees.FindAsync(employeeId);
        return Ok(new DailyReportDto(report.DailyTourReportId, report.TourPlanId, report.EmployeeId, employee!.FullName,
            report.ReportDate, report.VisitedLocations, report.WorkSummary, report.OutcomeSummary, report.NextDayPlan, report.CreatedAt));
    }
}
