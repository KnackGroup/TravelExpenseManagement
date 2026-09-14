namespace TravelExpense.Api.Services.Notifications;

public record EmailSendResult(bool Success, string? Error);

public interface IEmailSender
{
    /// <summary>Sends immediately using whatever EmailSettings row is currently configured.
    /// Throws SmtpNotConfiguredException if sending is disabled/unconfigured - callers (the
    /// background dispatcher, or a test-send endpoint) decide how to surface that.</summary>
    Task<EmailSendResult> SendAsync(string toAddress, string? ccAddress, string subject, string bodyHtml, CancellationToken ct = default);
}

public class SmtpNotConfiguredException : Exception
{
    public SmtpNotConfiguredException(string message) : base(message) { }
}
