using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http;
using TravelExpense.Api.Data;
using TravelExpense.Api.Services.Security;

namespace TravelExpense.Api.Services.Ai;

public class AiProviderFactory : IAiProviderFactory
{
    private readonly AppDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;

    public AiProviderFactory(AppDbContext db, ISecretProtector protector, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _protector = protector;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IAiTextProvider> GetTextProviderAsync(CancellationToken ct = default) =>
        (IAiTextProvider)await BuildAsync(ct);

    public async Task<IAiVisionProvider> GetVisionProviderAsync(CancellationToken ct = default) =>
        (IAiVisionProvider)await BuildAsync(ct);

    private async Task<object> BuildAsync(CancellationToken ct)
    {
        var settings = await _db.AiSettings.OrderByDescending(s => s.AiSettingsId).FirstOrDefaultAsync(ct);
        if (settings == null || !settings.IsEnabled || settings.Provider == "None" || string.IsNullOrEmpty(settings.ApiKeyEncrypted) || string.IsNullOrEmpty(settings.Model))
            return new NullAiProvider();

        var apiKey = _protector.Unprotect(settings.ApiKeyEncrypted);
        var http = _httpClientFactory.CreateClient("ai-provider");
        http.Timeout = TimeSpan.FromSeconds(60);

        return new OpenAiCompatibleProvider(http, settings.Provider, apiKey, settings.ApiEndpoint, settings.Model);
    }
}
