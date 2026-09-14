namespace TravelExpense.Api.Services.Ai;

/// <summary>Text-generation provider used for natural-language tour-plan entry and AI trip
/// summaries. Implementations: OpenAiCompatibleProvider (works with OpenAI or Azure OpenAI,
/// selected by AiSettings.Provider) when configured, NullAiProvider otherwise.</summary>
public interface IAiTextProvider
{
    bool IsAvailable { get; }

    /// <param name="systemPrompt">Instructions for the model.</param>
    /// <param name="userPrompt">The actual content to act on.</param>
    /// <returns>Raw text completion.</returns>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

/// <summary>Vision provider used for receipt OCR/parsing.</summary>
public interface IAiVisionProvider
{
    bool IsAvailable { get; }

    Task<string> DescribeImageAsync(string systemPrompt, byte[] imageBytes, string contentType, CancellationToken ct = default);
}

/// <summary>Always-available no-op fallback so callers never have to null-check the provider
/// itself - they check IsAvailable and show a friendly "AI not configured yet" state.</summary>
public class NullAiProvider : IAiTextProvider, IAiVisionProvider
{
    public bool IsAvailable => false;

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
        throw new InvalidOperationException("No AI provider is configured (Admin > AI Settings).");

    public Task<string> DescribeImageAsync(string systemPrompt, byte[] imageBytes, string contentType, CancellationToken ct = default) =>
        throw new InvalidOperationException("No AI provider is configured (Admin > AI Settings).");
}

/// <summary>Reads the current (admin-configurable) AiSettings row and hands back a ready-to-
/// use provider - NullAiProvider if disabled/unconfigured, otherwise a live OpenAI/Azure
/// OpenAI-compatible client. Scoped per-request since settings can change at runtime.</summary>
public interface IAiProviderFactory
{
    Task<IAiTextProvider> GetTextProviderAsync(CancellationToken ct = default);
    Task<IAiVisionProvider> GetVisionProviderAsync(CancellationToken ct = default);
}
