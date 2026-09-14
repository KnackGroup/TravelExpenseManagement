namespace TravelExpense.Api.Dtos;

// ---------- AI-lite (no external key needed - computed straight from this database) ----------

public record ExceptionRiskAssessmentDto(
    int ExpenseLineItemId, string RiskLevel, string Recommendation, string Rationale,
    decimal? OveragePercent, int EmployeeExceptionCountLast90Days, decimal? EmployeeExceptionApprovalRate);

public record AdvanceSuggestionDto(int TourPlanId, decimal SuggestedAmount, string Currency, string Basis, int ComparableTripsCount);

public record AnomalyFlagDto(int ExpenseLineItemId, string FlagType, string Severity, string Message);

public record PolicyQuestionRequest(string Question, int RoleId);
public record PolicyAnswerDto(string Answer, bool Grounded);

// ---------- LLM-backed (bring-your-own-key via AiSettings) ----------

public record AiFeatureStatusDto(bool IsEnabled, string Provider);

public record ReceiptParseResultDto(
    bool IsAvailable, decimal? Amount, DateOnly? ExpenseDate, string? Vendor,
    string? SuggestedCategoryCode, double? Confidence, string? Notes);

public record ParseTourPlanTextRequest(string Text);
public record ParsedTourPlanStopDto(DateOnly? VisitDate, string Location, string? StateOrCountry, string? PurposeNotes);
public record ParseTourPlanTextResultDto(bool IsAvailable, string? Title, string? PurposeOfVisit, DateOnly? StartDate, DateOnly? EndDate, List<ParsedTourPlanStopDto> Stops, string? Notes);

public record TripSummaryResultDto(bool AiGenerated, string Summary);

// ---------- Admin: AI settings ----------

public record AiSettingsDto(bool IsEnabled, string Provider, string? ApiEndpoint, bool HasApiKey, string? Model);
public record UpdateAiSettingsRequest(bool IsEnabled, string Provider, string? ApiEndpoint, string? NewApiKey, string? Model);
