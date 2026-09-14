namespace TravelExpense.Api.Data.Entities;

public static class EmailAuthMode
{
    public const string Basic = "Basic";
    public const string OAuth2 = "OAuth2";
}

/// <summary>Single-row (in practice) dynamic SMTP configuration - editable from Admin >
/// Notifications without a redeploy. Secrets are stored encrypted via ASP.NET Core Data
/// Protection (see Services/Security/SecretProtector.cs), never in plain text.</summary>
public class EmailSettings : IUpdateTimestamped
{
    public int EmailSettingsId { get; set; }
    public bool IsEnabled { get; set; }
    public string SmtpHost { get; set; } = "smtp.office365.com";
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string AuthMode { get; set; } = EmailAuthMode.Basic;
    public string? SmtpUsername { get; set; }
    public string? SmtpSecretEncrypted { get; set; }
    public string? OAuthTenantId { get; set; }
    public string FromAddress { get; set; } = "noreply@example.com";
    public string FromName { get; set; } = "Travel & Expense System";
    public int? UpdatedByEmployeeId { get; set; }
    public DateTime UpdatedAt { get; set; }

    void IUpdateTimestamped.StampUpdated(DateTime utcNow) => UpdatedAt = utcNow;
}

public class EmailTemplate : IUpdateTimestamped
{
    public int EmailTemplateId { get; set; }
    public string EventType { get; set; } = default!;
    public string? Description { get; set; }
    public string Subject { get; set; } = default!;
    public string BodyHtml { get; set; } = default!;
    public bool IsEnabled { get; set; } = true;
    public int? UpdatedByEmployeeId { get; set; }
    public DateTime UpdatedAt { get; set; }

    void IUpdateTimestamped.StampUpdated(DateTime utcNow) => UpdatedAt = utcNow;
}

public class EmployeeNotificationPreference
{
    public int EmployeeNotificationPreferenceId { get; set; }
    public int EmployeeId { get; set; }
    public string EventType { get; set; } = default!;
    public bool IsEnabled { get; set; } = true;

    public Employee Employee { get; set; } = default!;
}

public static class EmailOutboxStatus
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";
}

public class EmailOutboxEntry : ICreationTimestamped
{
    public int EmailOutboxId { get; set; }
    public string ToAddress { get; set; } = default!;
    public string? CcAddress { get; set; }
    public string Subject { get; set; } = default!;
    public string BodyHtml { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public string? RelatedEntityType { get; set; }
    public int? RelatedEntityId { get; set; }
    public string Status { get; set; } = EmailOutboxStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }

    void ICreationTimestamped.StampCreated(DateTime utcNow) => CreatedAt = utcNow;
}

/// <summary>Known workflow event types. Keep these string values in sync with
/// db/seed.sql's EmailTemplates.EventType rows.</summary>
public static class NotificationEventType
{
    public const string TourPlanSubmitted = "TourPlanSubmitted";
    public const string AdvanceRequested = "AdvanceRequested";
    public const string AdvanceApprovedBySuperior = "AdvanceApprovedBySuperior";
    public const string AdvanceRejected = "AdvanceRejected";
    public const string AdvanceDisbursed = "AdvanceDisbursed";
    public const string ExpenseReportSubmitted = "ExpenseReportSubmitted";
    public const string ExpensePolicyExceptionRaised = "ExpensePolicyExceptionRaised";
    public const string ExpenseReportDecided = "ExpenseReportDecided";
    public const string DailyReportReminder = "DailyReportReminder";
    public const string PendingApprovalDigest = "PendingApprovalDigest";
}

public class AiSettings : IUpdateTimestamped
{
    public int AiSettingsId { get; set; }
    public bool IsEnabled { get; set; }
    public string Provider { get; set; } = "None"; // None | OpenAI | AzureOpenAI
    public string? ApiEndpoint { get; set; }
    public string? ApiKeyEncrypted { get; set; }
    public string? Model { get; set; }
    public int? UpdatedByEmployeeId { get; set; }
    public DateTime UpdatedAt { get; set; }

    void IUpdateTimestamped.StampUpdated(DateTime utcNow) => UpdatedAt = utcNow;
}
