using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;

namespace TravelExpense.Api.Services.Notifications;

public record NotificationRecipient(int? EmployeeId, string Email, string Name);

/// <summary>
/// The single place workflow code calls to notify people. It never sends mail itself -
/// it renders the (admin-editable) template for the event and drops one row per recipient
/// into EmailOutbox, which EmailDispatcherHostedService flushes in the background. That
/// keeps the triggering request fast and makes sending retry-safe.
///
/// Two independent on/off switches are honored, both "dynamic settings" per the requirement:
///   1. EmailTemplates.IsEnabled - a global switch per event type (Admin > Notifications).
///   2. EmployeeNotificationPreferences - a per-employee opt-out for a given event type.
/// A recipient with no EmployeeId (e.g. a role-based mailbox) only goes through switch 1.
/// </summary>
public class NotificationService
{
    private readonly AppDbContext _db;
    private readonly string _appUrl;

    public NotificationService(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _appUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";
    }

    public async Task NotifyAsync(
        string eventType,
        IEnumerable<NotificationRecipient> recipients,
        IDictionary<string, string?> placeholders,
        string? relatedEntityType = null,
        int? relatedEntityId = null)
    {
        var template = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.EventType == eventType);
        if (template == null || !template.IsEnabled) return; // no template, or admin has switched this event off entirely

        var recipientList = recipients.Where(r => !string.IsNullOrWhiteSpace(r.Email)).ToList();
        if (recipientList.Count == 0) return;

        var employeeIds = recipientList.Where(r => r.EmployeeId.HasValue).Select(r => r.EmployeeId!.Value).Distinct().ToList();
        var optOuts = employeeIds.Count == 0
            ? new HashSet<int>()
            : (await _db.EmployeeNotificationPreferences
                .Where(p => employeeIds.Contains(p.EmployeeId) && p.EventType == eventType && !p.IsEnabled)
                .Select(p => p.EmployeeId)
                .ToListAsync()).ToHashSet();

        foreach (var recipient in recipientList)
        {
            if (recipient.EmployeeId.HasValue && optOuts.Contains(recipient.EmployeeId.Value))
                continue; // this specific person opted out of this event type

            var mergedPlaceholders = new Dictionary<string, string?>(placeholders, StringComparer.OrdinalIgnoreCase)
            {
                ["RecipientName"] = recipient.Name,
                ["AppUrl"] = _appUrl
            };

            _db.EmailOutbox.Add(new EmailOutboxEntry
            {
                ToAddress = recipient.Email,
                Subject = Render(template.Subject, mergedPlaceholders),
                BodyHtml = Render(template.BodyHtml, mergedPlaceholders),
                EventType = eventType,
                RelatedEntityType = relatedEntityType,
                RelatedEntityId = relatedEntityId,
                Status = EmailOutboxStatus.Pending
            });
        }

        await _db.SaveChangesAsync();
    }

    private static string Render(string template, IDictionary<string, string?> placeholders)
    {
        var result = template;
        foreach (var kv in placeholders)
            result = result.Replace("{{" + kv.Key + "}}", kv.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        return result;
    }
}
