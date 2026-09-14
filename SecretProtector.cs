using Microsoft.AspNetCore.DataProtection;

namespace TravelExpense.Api.Services.Security;

/// <summary>
/// Encrypts secrets (SMTP password/client secret, AI API key) before they're stored in the
/// database, using ASP.NET Core's built-in Data Protection API - no extra package needed.
/// NOTE: by default, Data Protection keys are kept on local disk per-container. In the Docker
/// Compose setup here that means encrypted secrets only decrypt on the same API container/
/// volume that wrote them - see README for how to persist the key ring (a mounted volume, or
/// Azure Key Vault / Blob storage in production) if you redeploy the API container.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plainText);
    string? ProtectOrNull(string? plainText);
    string Unprotect(string protectedText);
    string? UnprotectOrNull(string? protectedText);
}

public class SecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public SecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("TravelExpense.Secrets.v1");
    }

    public string Protect(string plainText) => _protector.Protect(plainText);

    public string? ProtectOrNull(string? plainText) => string.IsNullOrEmpty(plainText) ? null : Protect(plainText);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);

    public string? UnprotectOrNull(string? protectedText) => string.IsNullOrEmpty(protectedText) ? null : Unprotect(protectedText);
}
