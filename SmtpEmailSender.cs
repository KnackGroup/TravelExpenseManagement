using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using MimeKit;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;
using TravelExpense.Api.Services.Security;

namespace TravelExpense.Api.Services.Notifications;

/// <summary>
/// Sends mail via SMTP (smtp.office365.com by default, but any SMTP host works) using
/// whichever EmailSettings row is configured in the database - so Admin > Notifications
/// controls this at runtime, no redeploy or appsettings edit required.
///
/// Two auth modes:
///  - Basic: SMTP AUTH username/app-password. Works if your Microsoft 365 tenant still allows
///    SMTP AUTH for the sending mailbox (Exchange admin center > mailbox > "Manage email apps").
///  - OAuth2: acquires an app-only access token via MSAL client-credentials flow (ClientId =
///    SmtpUsername, ClientSecret = the encrypted secret, TenantId = OAuthTenantId) for scope
///    https://outlook.office365.com/.default, then authenticates SMTP with XOAUTH2. This is
///    Microsoft's documented replacement for Basic auth, but it requires your Exchange admin
///    to grant the app registration the SMTP.SendAsApp role for the sending mailbox first
///    (New-ServicePrincipal / New-ManagementRoleAssignment in Exchange Online PowerShell) -
///    see README > "Configuring Outlook / Microsoft 365 email".
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly AppDbContext _db;
    private readonly ISecretProtector _protector;

    public SmtpEmailSender(AppDbContext db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<EmailSendResult> SendAsync(string toAddress, string? ccAddress, string subject, string bodyHtml, CancellationToken ct = default)
    {
        var settings = await _db.EmailSettings.OrderByDescending(s => s.EmailSettingsId).FirstOrDefaultAsync(ct);
        if (settings == null || !settings.IsEnabled)
            throw new SmtpNotConfiguredException("Email sending is disabled (Admin > Notifications > Email Settings).");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        if (!string.IsNullOrWhiteSpace(ccAddress))
            message.Cc.Add(MailboxAddress.Parse(ccAddress));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = bodyHtml }.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort,
                settings.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);

            if (settings.AuthMode == EmailAuthMode.OAuth2)
            {
                var token = await AcquireOAuthTokenAsync(settings, ct);
                var oauth2 = new SaslMechanismOAuth2(settings.SmtpUsername ?? settings.FromAddress, token);
                await client.AuthenticateAsync(oauth2, ct);
            }
            else if (!string.IsNullOrEmpty(settings.SmtpUsername))
            {
                var password = _protector.UnprotectOrNull(settings.SmtpSecretEncrypted) ?? string.Empty;
                await client.AuthenticateAsync(settings.SmtpUsername, password, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            return new EmailSendResult(true, null);
        }
        catch (Exception ex)
        {
            return new EmailSendResult(false, ex.Message);
        }
    }

    private async Task<string> AcquireOAuthTokenAsync(EmailSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(settings.SmtpUsername) || string.IsNullOrEmpty(settings.OAuthTenantId))
            throw new SmtpNotConfiguredException("OAuth2 mode requires SmtpUsername (app client id) and OAuthTenantId to be set.");

        var clientSecret = _protector.UnprotectOrNull(settings.SmtpSecretEncrypted)
            ?? throw new SmtpNotConfiguredException("OAuth2 mode requires the app's client secret to be set.");

        var app = ConfidentialClientApplicationBuilder
            .Create(settings.SmtpUsername)
            .WithClientSecret(clientSecret)
            .WithAuthority(new Uri($"https://login.microsoftonline.com/{settings.OAuthTenantId}"))
            .Build();

        var result = await app.AcquireTokenForClient(new[] { "https://outlook.office365.com/.default" })
            .ExecuteAsync(ct);

        return result.AccessToken;
    }
}
