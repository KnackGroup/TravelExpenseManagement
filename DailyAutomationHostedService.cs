using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;

namespace TravelExpense.Api.Services.Notifications;

/// <summary>
/// Two pieces of "reduce the manual work" automation, both driven by the same notification
/// pipeline as everything else (so the same enable/disable and per-employee opt-out rules
/// apply):
///
///  1. Pending-approval digest: once a day, every manager/Accounts user with something
///     waiting on them gets a single summary email instead of having to remember to check.
///  2. Daily-report reminder: employees who are mid-trip today but haven't logged today's
///     field report yet get a nudge - this is what actually gets daily reports filled in
///     consistently instead of "I'll do it at the end of the week."
///
/// Runs an hourly check and fires once per UTC day at DigestHourUtc (default 03:00 UTC,
/// override via Notifications:DigestHourUtc) - a simple in-memory "did we already run today"
/// guard is enough here since this is a single-instance API; a multi-instance deployment
/// should move this guard into the database (e.g. a LastDigestRunDate row) to avoid double-sends.
/// </summary>
public class DailyAutomationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyAutomationHostedService> _logger;
    private readonly int _digestHourUtc;
    private DateOnly? _lastRunDate;

    public DailyAutomationHostedService(IServiceScopeFactory scopeFactory, ILogger<DailyAutomationHostedService> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _digestHourUtc = int.TryParse(configuration["Notifications:DigestHourUtc"], out var h) ? h : 3;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var today = DateOnly.FromDateTime(now);
                if (now.Hour == _digestHourUtc && _lastRunDate != today)
                {
                    await RunDailyJobsAsync(stoppingToken);
                    _lastRunDate = today;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily automation cycle failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
            catch (TaskCanceledException) { }
        }
    }

    private async Task RunDailyJobsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        await SendPendingApprovalDigestsAsync(db, notifications, ct);
        await SendDailyReportRemindersAsync(db, notifications, ct);
    }

    private static async Task SendPendingApprovalDigestsAsync(AppDbContext db, NotificationService notifications, CancellationToken ct)
    {
        var pendingAdvancesBySuperior = await db.AdvanceRequests
            .Where(a => a.Status == AdvanceRequestStatus.Pending)
            .Include(a => a.Employee)
            .GroupBy(a => a.Employee.ManagerEmployeeId)
            .Where(g => g.Key != null)
            .Select(g => new { ManagerId = g.Key!.Value, Count = g.Count() })
            .ToListAsync(ct);

        var pendingExceptionsByManager = await db.ExpenseLineItems
            .Where(li => li.IsPolicyException && li.LineStatus == LineStatus.Pending)
            .Include(li => li.ExpenseReport).ThenInclude(r => r.Employee)
            .GroupBy(li => li.ExpenseReport.Employee.ManagerEmployeeId)
            .Where(g => g.Key != null)
            .Select(g => new { ManagerId = g.Key!.Value, Count = g.Count() })
            .ToListAsync(ct);

        var managerTotals = new Dictionary<int, int>();
        foreach (var g in pendingAdvancesBySuperior) managerTotals[g.ManagerId] = managerTotals.GetValueOrDefault(g.ManagerId) + g.Count;
        foreach (var g in pendingExceptionsByManager) managerTotals[g.ManagerId] = managerTotals.GetValueOrDefault(g.ManagerId) + g.Count;

        var pendingAtAccounts = await db.AdvanceRequests.CountAsync(a => a.Status == AdvanceRequestStatus.ApprovedBySuperior, ct);
        var accountsUsers = pendingAtAccounts > 0
            ? await db.Employees.Include(e => e.Role).Where(e => e.IsActive && e.Role.RoleType == RoleType.Accounts).ToListAsync(ct)
            : new List<Employee>();

        if (managerTotals.Count == 0 && accountsUsers.Count == 0) return;

        var managerIds = managerTotals.Keys.ToList();
        var managers = await db.Employees.Where(e => managerIds.Contains(e.EmployeeId)).ToListAsync(ct);

        foreach (var manager in managers)
        {
            await notifications.NotifyAsync(
                NotificationEventType.PendingApprovalDigest,
                new[] { new NotificationRecipient(manager.EmployeeId, manager.Email, manager.FullName) },
                new Dictionary<string, string?> { ["Amount"] = managerTotals[manager.EmployeeId].ToString() });
        }

        foreach (var accountsUser in accountsUsers)
        {
            await notifications.NotifyAsync(
                NotificationEventType.PendingApprovalDigest,
                new[] { new NotificationRecipient(accountsUser.EmployeeId, accountsUser.Email, accountsUser.FullName) },
                new Dictionary<string, string?> { ["Amount"] = pendingAtAccounts.ToString() });
        }
    }

    private static async Task SendDailyReportRemindersAsync(AppDbContext db, NotificationService notifications, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var activeTrips = await db.TourPlans
            .Include(t => t.Employee)
            .Where(t => t.Status == TourPlanStatus.Approved && t.StartDate <= today && t.EndDate >= today)
            .ToListAsync(ct);

        if (activeTrips.Count == 0) return;

        var tripIds = activeTrips.Select(t => t.TourPlanId).ToList();
        var reportedTripIds = (await db.DailyTourReports
            .Where(d => tripIds.Contains(d.TourPlanId) && d.ReportDate == today)
            .Select(d => d.TourPlanId)
            .ToListAsync(ct)).ToHashSet();

        foreach (var trip in activeTrips.Where(t => !reportedTripIds.Contains(t.TourPlanId)))
        {
            await notifications.NotifyAsync(
                NotificationEventType.DailyReportReminder,
                new[] { new NotificationRecipient(trip.EmployeeId, trip.Employee.Email, trip.Employee.FullName) },
                new Dictionary<string, string?> { ["TourPlanTitle"] = trip.Title },
                relatedEntityType: "TourPlan", relatedEntityId: trip.TourPlanId);
        }
    }
}
