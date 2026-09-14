namespace TravelExpense.Api.Dtos;

public record TourPlanStopRequest(DateOnly VisitDate, string Location, string? StateOrCountry, string? PurposeNotes);

public record CreateTourPlanRequest(
    int ChannelId, string Title, string? PurposeOfVisit,
    DateOnly StartDate, DateOnly EndDate, List<TourPlanStopRequest> Stops);

public record TourPlanDto(
    int TourPlanId, int EmployeeId, string EmployeeName, int ChannelId, string ChannelName,
    string Title, string? PurposeOfVisit, DateOnly StartDate, DateOnly EndDate, string Status,
    List<TourPlanStopRequest> Stops, DateTime CreatedAt);
