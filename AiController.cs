using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Auth;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;
using TravelExpense.Api.Dtos;
using TravelExpense.Api.Services;
using TravelExpense.Api.Services.Ai;
using TravelExpense.Api.Services.Security;

namespace TravelExpense.Api.Controllers;

/// <summary>
/// AI features in two tiers:
///   1. "AI-lite" (AiInsightService) - risk scoring, anomaly detection, advance suggestions,
///      policy Q&A. Computed from this database. Always on, no external key needed.
///   2. LLM-backed (IAiProviderFactory) - receipt OCR, natural-language tour-plan entry, AI
///      trip summaries. Only functional once Admin > AI Settings has a provider + key; every
///      endpoint here degrades gracefully (IsAvailable = false) rather than erroring when it
///      isn't configured, except the trip-summary endpoint which has a genuinely useful
///      no-AI fallback (extractive summary of daily reports).
/// </summary>
[ApiController]
[Route("api/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AiInsightService _insights;
    private readonly IAiProviderFactory _providerFactory;
    private readonly ApprovalRoutingService _routing;
    private readonly ISecretProtector _protector;

    public AiController(AppDbContext db, AiInsightService insights, IAiProviderFactory providerFactory, ApprovalRoutingService routing, ISecretProtector protector)
    {
        _db = db;
        _insights = insights;
        _providerFactory = providerFactory;
        _routing = routing;
        _protector = protector;
    }

    // ================= AI-lite: always on =================

    [HttpGet("insights/exception/{lineItemId:int}")]
    public async Task<ActionResult<ExceptionRiskAssessmentDto>> GetExceptionRisk(int lineItemId)
    {
        var result = await _insights.AssessExceptionRiskAsync(lineItemId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("insights/advance-suggestion/{tourPlanId:int}")]
    public async Task<ActionResult<AdvanceSuggestionDto>> GetAdvanceSuggestion(int tourPlanId)
    {
        var meId = User.GetEmployeeId();
        var plan = await _db.TourPlans.FirstOrDefaultAsync(t => t.TourPlanId == tourPlanId);
        if (plan == null) return NotFound();
        if (plan.EmployeeId != meId && !await _routing.IsInReportingChainAsync(plan.EmployeeId, meId))
            return Forbid();

        return Ok(await _insights.SuggestAdvanceAmountAsync(tourPlanId));
    }

    [HttpGet("insights/anomalies/{expenseReportId:int}")]
    public async Task<ActionResult<List<AnomalyFlagDto>>> GetAnomalies(int expenseReportId)
    {
        var meId = User.GetEmployeeId();
        var report = await _db.ExpenseReports.FirstOrDefaultAsync(r => r.ExpenseReportId == expenseReportId);
        if (report == null) return NotFound();
        if (report.EmployeeId != meId && !await _routing.IsInReportingChainAsync(report.EmployeeId, meId))
            return Forbid();

        return Ok(await _insights.DetectAnomaliesAsync(expenseReportId));
    }

    [HttpPost("policy-assistant/ask")]
    public async Task<ActionResult<PolicyAnswerDto>> AskPolicyQuestion(PolicyQuestionRequest request)
    {
        return Ok(await _insights.AnswerPolicyQuestionAsync(request.Question, request.RoleId));
    }

    // ================= LLM-backed: bring your own key =================

    [HttpGet("status")]
    public async Task<ActionResult<AiFeatureStatusDto>> GetStatus()
    {
        var settings = await _db.AiSettings.OrderByDescending(s => s.AiSettingsId).FirstOrDefaultAsync();
        return Ok(new AiFeatureStatusDto(settings?.IsEnabled ?? false, settings?.Provider ?? "None"));
    }

    [HttpPost("receipts/parse")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<ReceiptParseResultDto>> ParseReceipt(IFormFile file)
    {
        var vision = await _providerFactory.GetVisionProviderAsync();
        if (!vision.IsAvailable)
            return Ok(new ReceiptParseResultDto(false, null, null, null, null, null, "AI receipt scanning isn't configured yet - ask an admin to add an AI provider under Admin > AI Settings. You can still fill this in manually."));

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);

        var categories = await _db.ExpenseCategories.Where(c => c.IsActive).Select(c => c.CategoryCode).ToListAsync();
        var instruction =
            "This image is a travel expense receipt. Extract the total amount, the date, and the vendor/merchant name. " +
            $"Also guess the best matching expense category code from this list: {string.Join(", ", categories)}. " +
            "Respond with ONLY a JSON object, no markdown, in exactly this shape: " +
            "{\"amount\": number, \"date\": \"YYYY-MM-DD\", \"vendor\": string, \"categoryCode\": string, \"confidence\": number between 0 and 1}. " +
            "If a field can't be determined, use null for it.";

        try
        {
            var raw = await vision.DescribeImageAsync(instruction, ms.ToArray(), file.ContentType);
            var json = ExtractJson(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            decimal? amount = root.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDecimal() : null;
            DateOnly? date = root.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String && DateOnly.TryParse(d.GetString(), out var parsedDate) ? parsedDate : null;
            string? vendor = root.TryGetProperty("vendor", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            string? categoryCode = root.TryGetProperty("categoryCode", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            double? confidence = root.TryGetProperty("confidence", out var conf) && conf.ValueKind == JsonValueKind.Number ? conf.GetDouble() : null;

            return Ok(new ReceiptParseResultDto(true, amount, date, vendor, categoryCode, confidence, null));
        }
        catch (Exception ex)
        {
            return Ok(new ReceiptParseResultDto(false, null, null, null, null, null, $"Couldn't read that receipt automatically ({ex.Message}). Please fill it in manually."));
        }
    }

    [HttpPost("tour-plans/parse-text")]
    public async Task<ActionResult<ParseTourPlanTextResultDto>> ParseTourPlanText(ParseTourPlanTextRequest request)
    {
        var text = await _providerFactory.GetTextProviderAsync();
        if (!text.IsAvailable)
            return Ok(new ParseTourPlanTextResultDto(false, null, null, null, null, new List<ParsedTourPlanStopDto>(),
                "Natural-language tour plan entry isn't configured yet - ask an admin to add an AI provider under Admin > AI Settings."));

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var instruction =
            $"Today's date is {today}. Parse the following free-text trip description into structured tour-plan data. " +
            "Respond with ONLY a JSON object, no markdown, in exactly this shape: " +
            "{\"title\": string, \"purposeOfVisit\": string, \"startDate\": \"YYYY-MM-DD\" or null, \"endDate\": \"YYYY-MM-DD\" or null, " +
            "\"stops\": [{\"visitDate\": \"YYYY-MM-DD\" or null, \"location\": string, \"stateOrCountry\": string or null, \"purposeNotes\": string or null}]}. " +
            "Resolve relative dates against today's date.";

        try
        {
            var raw = await text.CompleteAsync(instruction, request.Text);
            var json = ExtractJson(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            string? purpose = root.TryGetProperty("purposeOfVisit", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            DateOnly? start = root.TryGetProperty("startDate", out var s) && s.ValueKind == JsonValueKind.String && DateOnly.TryParse(s.GetString(), out var sd) ? sd : null;
            DateOnly? end = root.TryGetProperty("endDate", out var e) && e.ValueKind == JsonValueKind.String && DateOnly.TryParse(e.GetString(), out var ed) ? ed : null;

            var stops = new List<ParsedTourPlanStopDto>();
            if (root.TryGetProperty("stops", out var stopsEl) && stopsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var stopEl in stopsEl.EnumerateArray())
                {
                    var location = stopEl.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.String ? loc.GetString() : null;
                    if (string.IsNullOrWhiteSpace(location)) continue;

                    DateOnly? visitDate = stopEl.TryGetProperty("visitDate", out var vd) && vd.ValueKind == JsonValueKind.String && DateOnly.TryParse(vd.GetString(), out var pvd) ? pvd : null;
                    string? state = stopEl.TryGetProperty("stateOrCountry", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
                    string? notes = stopEl.TryGetProperty("purposeNotes", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString() : null;
                    stops.Add(new ParsedTourPlanStopDto(visitDate, location!, state, notes));
                }
            }

            return Ok(new ParseTourPlanTextResultDto(true, title, purpose, start, end, stops, null));
        }
        catch (Exception ex)
        {
            return Ok(new ParseTourPlanTextResultDto(false, null, null, null, null, new List<ParsedTourPlanStopDto>(),
                $"Couldn't parse that automatically ({ex.Message}). Please fill in the form manually."));
        }
    }

    [HttpGet("tour-plans/{tourPlanId:int}/summary")]
    public async Task<ActionResult<TripSummaryResultDto>> GetTripSummary(int tourPlanId)
    {
        var dailyReports = await _db.DailyTourReports
            .Where(d => d.TourPlanId == tourPlanId)
            .OrderBy(d => d.ReportDate)
            .ToListAsync();

        if (dailyReports.Count == 0)
            return Ok(new TripSummaryResultDto(false, "No daily reports have been logged for this trip yet."));

        var text = await _providerFactory.GetTextProviderAsync();
        if (text.IsAvailable)
        {
            try
            {
                var combined = string.Join("\n", dailyReports.Select(d =>
                    $"{d.ReportDate}: visited {d.VisitedLocations}. Work: {d.WorkSummary}. Outcome: {d.OutcomeSummary}."));
                var summary = await text.CompleteAsync(
                    "Summarize this multi-day sales tour into a concise 3-5 sentence outcome paragraph for a manager. Be factual, no fluff.",
                    combined);
                return Ok(new TripSummaryResultDto(true, summary.Trim()));
            }
            catch
            {
                // fall through to the extractive fallback below
            }
        }

        // No-AI fallback: a genuinely useful extractive summary, always available.
        var outcomes = dailyReports.Where(d => !string.IsNullOrWhiteSpace(d.OutcomeSummary)).Select(d => d.OutcomeSummary!.Trim());
        var extractive = string.Join(" ", outcomes);
        if (string.IsNullOrWhiteSpace(extractive))
            extractive = $"{dailyReports.Count} daily report(s) logged, but none include an outcome summary yet.";

        return Ok(new TripSummaryResultDto(false, extractive));
    }

    // ================= Admin: AI settings =================

    [HttpGet("settings")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<ActionResult<AiSettingsDto>> GetSettings()
    {
        var settings = await _db.AiSettings.OrderByDescending(s => s.AiSettingsId).FirstOrDefaultAsync();
        settings ??= new AiSettings();
        return Ok(new AiSettingsDto(settings.IsEnabled, settings.Provider, settings.ApiEndpoint, !string.IsNullOrEmpty(settings.ApiKeyEncrypted), settings.Model));
    }

    [HttpPut("settings")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<ActionResult<AiSettingsDto>> UpdateSettings(UpdateAiSettingsRequest request)
    {
        var employeeId = User.GetEmployeeId();
        var settings = await _db.AiSettings.OrderByDescending(s => s.AiSettingsId).FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new AiSettings();
            _db.AiSettings.Add(settings);
        }

        settings.IsEnabled = request.IsEnabled;
        settings.Provider = request.Provider;
        settings.ApiEndpoint = request.ApiEndpoint;
        settings.Model = request.Model;
        settings.UpdatedByEmployeeId = employeeId;
        settings.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(request.NewApiKey))
            settings.ApiKeyEncrypted = _protector.Protect(request.NewApiKey);

        await _db.SaveChangesAsync();
        return Ok(new AiSettingsDto(settings.IsEnabled, settings.Provider, settings.ApiEndpoint, !string.IsNullOrEmpty(settings.ApiKeyEncrypted), settings.Model));
    }

    private static string ExtractJson(string raw)
    {
        // Models sometimes wrap JSON in ```json fences despite instructions - strip if present.
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            trimmed = trimmed[(firstNewline + 1)..];
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) trimmed = trimmed[..lastFence];
        }
        return trimmed.Trim();
    }
}
