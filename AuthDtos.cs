namespace TravelExpense.Api.Dtos;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, EmployeeSummaryDto Employee);

public record EmployeeSummaryDto(
    int EmployeeId,
    string EmployeeCode,
    string FullName,
    string Email,
    int RoleId,
    string RoleCode,
    string RoleName,
    string RoleType,
    int? ChannelId,
    string? ChannelName,
    int? HierarchyLevel,
    int? ManagerEmployeeId,
    string? ManagerName);
