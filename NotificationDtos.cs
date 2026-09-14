namespace TravelExpense.Api.Dtos;

public record EmailSettingsDto(
    bool IsEnabled, string SmtpHost, int SmtpPort, bool EnableSsl, string AuthMode,
    string? SmtpUsername, bool HasSecret, string? OAuthTenantId, string FromAddress, string FromName);

public record UpdateEmailSettingsRequest(
    bool IsEnabled, string SmtpHost, int SmtpPort, bool EnableSsl, string AuthMode,
    string? SmtpUsername, string? NewSecret, string? OAuthTenantId, string FromAddress, string FromName);

public record TestSendEmailRequest(string ToAddress);

public record EmailTemplateDto(int EmailTemplateId, string EventType, string? Description, string Subject, string BodyHtml, bool IsEnabled, DateTime UpdatedAt);

public record UpdateEmailTemplateRequest(string Subject, string BodyHtml, bool IsEnabled);

public record NotificationPreferenceDto(string EventType, string? Description, bool GloballyEnabled, bool IsEnabledForMe);

public record UpdateNotificationPreferenceRequest(string EventType, bool IsEnabled);
