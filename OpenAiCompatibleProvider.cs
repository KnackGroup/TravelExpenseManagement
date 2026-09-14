using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelExpense.Api.Services.Ai;

/// <summary>
/// Chat-completions client that works against either OpenAI's API or an Azure OpenAI
/// deployment - the two "bring your own key" options offered in Admin > AI Settings. Used for
/// both text completion (natural-language tour-plan parsing, trip summaries) and vision
/// (receipt OCR), since both providers expose the same multimodal chat-completions shape.
/// Request bodies are built as plain anonymous objects (rather than typed DTOs) to match the
/// OpenAI wire format exactly with no risk of a conditional-serialization edge case.
/// </summary>
public class OpenAiCompatibleProvider : IAiTextProvider, IAiVisionProvider
{
    private readonly HttpClient _http;
    private readonly string _provider; // "OpenAI" | "AzureOpenAI"
    private readonly string _apiKey;
    private readonly string? _endpoint;
    private readonly string _model;

    public bool IsAvailable => true;

    public OpenAiCompatibleProvider(HttpClient http, string provider, string apiKey, string? endpoint, string model)
    {
        _http = http;
        _provider = provider;
        _apiKey = apiKey;
        _endpoint = endpoint;
        _model = model;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var body = new
        {
            model = _model,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        return await SendAsync(body, ct);
    }

    public async Task<string> DescribeImageAsync(string instruction, byte[] imageBytes, string contentType, CancellationToken ct = default)
    {
        var dataUrl = $"data:{contentType};base64,{Convert.ToBase64String(imageBytes)}";

        var body = new
        {
            model = _model,
            temperature = 0.1,
            messages = new object[]
            {
                new { role = "system", content = "You extract structured data from images and respond only in the exact format requested." },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = instruction },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            }
        };

        return await SendAsync(body, ct);
    }

    private async Task<string> SendAsync(object body, CancellationToken ct)
    {
        HttpRequestMessage request;
        if (_provider == "AzureOpenAI")
        {
            var baseUrl = (_endpoint ?? throw new InvalidOperationException("AzureOpenAI requires an endpoint.")).TrimEnd('/');
            var url = $"{baseUrl}/openai/deployments/{_model}/chat/completions?api-version=2024-06-01";
            request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("api-key", _apiKey);
        }
        else
        {
            var baseUrl = string.IsNullOrWhiteSpace(_endpoint) ? "https://api.openai.com/v1" : _endpoint!.TrimEnd('/');
            request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        request.Content = JsonContent.Create(body);

        var response = await _http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"AI provider request failed ({(int)response.StatusCode}): {raw}");

        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return content ?? string.Empty;
    }
}
