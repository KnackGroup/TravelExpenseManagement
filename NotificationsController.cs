using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Auth;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;
using TravelExpense.Api.Dtos;
using TravelExpense.Api.Services.Notifications;
using TravelExpense.Api.Services.Security;

namespace TravelExpense.Api.Controllers;

/// <summary>
/// Everything the requirement asked for around "dynamic email configuration": SMTP settings
/// (Outlook/Microsoft 365, basic or OAuth2), per-event templates, a test-send button, and
/// per-employee opt-out - all editable at runtime from Admin > Notifications, no redeploy.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IEmailSender _emailSender;

    public NotificationsController(AppDbContext db, ISecretProtector protector, IEmailSender emailSender)
    {
        _db = db;
        _protector = protector;
        _emailSender = emailSender;
    }

    // ---------- Email settings (Admin only) ----------

    [HttpGet("settings")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<ActionResult<EmailSettingsDto>> GetSettings()
    {
        var settings = await _db.EmailSettings.OrderByDescending(s => s.EmailSettingsId).FirstOrDefaultAsync();
        settings ??= new EmailSettings();
        return Ok(ToDto(settings));
    }

    [HttpPut("settings")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<ActionResult<EmailSettingsDto>> UpdateSettings(UpdateEmailSettingsRequest request)
    {
        var employeeId = User.GetEmployeeId();
        var settings = await _db.EmailSettings.OrderByDescending(s => s.EmailSettingsId).FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new EmailSettings();
            _db.EmailSettings.Add(settings);
        }

        settings.IsEnabled = request.IsEnabled;
        settings.SmtpHost = request.SmtpHost;
        settings.SmtpPort = request.SmtpPort;
        settings.EnableSsl = request.EnableSsl;
        settings.AuthMode = request.AuthMode;
        settings.SmtpUsername = request.SmtpUsername;
        settings.OAuthTenantId = request.OAuthTenantId;
        settings.FromAddress = request.FromAddress;
        settings.FromName = request.FromName;
        settings.UpdatedByEmployeeId = employeeId;
        settings.UpdatedAt = DateTime.UtcNow;

        // Only overwrite the stored secret if a new one was actually supplied - so re-saving
        // the form without retyping the password doesn't wipe it.
        if (!string.IsNullOrEmpty(request.NewSecret))
            settings.SmtpSecretEncrypted = _protector.Protect(request.NewSecret);

        await _db.SaveChangesAsync();
        return Ok(ToDto(settings));
    }

    [HttpPost("settings/test-send")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<ActionResult> TestSend(TestSendEmailRequest request)
    {
        var result = await _emailSender.SendAsync(
            request.ToAddress, null,
            "Travel & Expense - test email",
            "<p>If you're reading this, your SMTP settings are working. 🎉</p>");

        if (!result.Success) return BadRequest(new { message = result.Error ?? "Send failed." });
        return Ok(new { message = "Test email sent." });
    }

    private static EmailSettingsDto ToDto(EmailSettings s) => new(
        s.IsEnabled, s.SmtpHost, s.SmtpPort, s.EnableSsl, s.AuthMode,
        s.SmtpUsername, !string.IsNullOrEmpty(s.SmtpSecretEncrypted), s.OAuthTenantId, s.FromAddress, s.FromName);

    // ---------- Templates ----------

    [HttpGet("templates")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<ActionResult<List<EmailTemplateDto>>> GetTemplates()
    {
        var templates = await _db.EmailTemplates.OrderBy(t => t.EventType)
            .Select(t => new EmailTemplateDto(t.EmailTemplateId, t.EventType, t.Description, t.Subject, t.BodyHtml, t.IsEnabled, t.UpdatedAt))
            .ToListAsync();
        return Ok(templates);
    }

    [HttpPut("templates/{eventType}")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<IActionResult> UpdateTemplate(string eventType, UpdateEmailTemplateRequest request)
    {
        var employeeId = User.GetEmployeeId();
        var template = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.EventType == eventType);
        if (template == null) return NotFound();

        template.Subject = request.Subject;
        template.BodyHtml = request.BodyHtml;
        template.IsEnabled = request.IsEnabled;
        template.UpdatedByEmployeeId = employeeId;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Per-employee preferences (self-service opt-out) ----------

    [HttpGet("preferences")]
    public async Task<ActionResult<List<NotificationPreferenceDto>>> GetMyPreferences()
    {
        var employeeId = User.GetEmployeeId();
        var templates = await _db.EmailTemplates.ToListAsync();
        var myOverrides = await _db.EmployeeNotificationPreferences
            .Where(p => p.EmployeeId == employeeId)
            .ToDictionaryAsync(p => p.EventType, p => p.IsEnabled);

        var result = templates.Select(t => new NotificationPreferenceDto(
            t.EventType, t.Description, t.IsEnabled,
            myOverrides.TryGetValue(t.EventType, out var pref) ? pref : true));

        return Ok(result);
    }

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdateMyPreference(UpdateNotificationPreferenceRequest request)
    {
        var employeeId = User.GetEmployeeId();
        var existing = await _db.EmployeeNotificationPreferences
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId && p.EventType == request.EventType);

        if (existing == null)
        {
            _db.EmployeeNotificationPreferences.Add(new EmployeeNotificationPreference
            {
                EmployeeId = employeeId,
                EventType = request.EventType,
                IsEnabled = request.IsEnabled
            });
        }
        else
        {
            existing.IsEnabled = request.IsEnabled;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
