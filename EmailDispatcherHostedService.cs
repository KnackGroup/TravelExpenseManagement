using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;

namespace TravelExpense.Api.Services.Notifications;

/// <summary>
/// Flushes EmailOutbox every 20 seconds: picks up to 20 Pending rows, sends each via
/// IEmailSender, marks Sent/Failed. A row is retried automatically (stays Pending) up to 5
/// attempts, then marked Failed so it stops being retried but remains visible for audit.
/// If email sending is disabled globally, pending rows are left untouched (they'll flush
/// automatically once an admin turns sending on).
/// </summary>
public class EmailDispatcherHostedService : BackgroundService
{
    private const int MaxAttempts = 5;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailDispatcherHostedService> _logger;

    public EmailDispatcherHostedService(IServiceScopeFactory scopeFactory, ILogger<EmailDispatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email dispatch cycle failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            }
            catch (TaskCanceledException) { }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var settings = await db.EmailSettings.OrderByDescending(s => s.EmailSettingsId).FirstOrDefaultAsync(ct);
        if (settings == null || !settings.IsEnabled) return;

        var batch = await db.EmailOutbox
            .Where(m => m.Status == EmailOutboxStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        foreach (var mail in batch)
        {
            try
            {
                var result = await sender.SendAsync(mail.ToAddress, mail.CcAddress, mail.Subject, mail.BodyHtml, ct);
                mail.Attempts++;
                if (result.Success)
                {
                    mail.Status = EmailOutboxStatus.Sent;
                    mail.SentAt = DateTime.UtcNow;
                }
                else
                {
                    mail.LastError = result.Error;
                    mail.Status = mail.Attempts >= MaxAttempts ? EmailOutboxStatus.Failed : EmailOutboxStatus.Pending;
                }
            }
            catch (Exception ex)
            {
                mail.Attempts++;
                mail.LastError = ex.Message;
                mail.Status = mail.Attempts >= MaxAttempts ? EmailOutboxStatus.Failed : EmailOutboxStatus.Pending;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
