namespace TravelExpense.Api.Dtos;

public record ChannelDto(int ChannelId, string ChannelCode, string ChannelName, bool IsActive);
public record CreateChannelRequest(string ChannelCode, string ChannelName);

public record RoleDto(
    int RoleId, int? ChannelId, string? ChannelName, string RoleCode, string RoleName,
    int? HierarchyLevel, string RoleType, bool IsActive);

public record CreateRoleRequest(int? ChannelId, string RoleCode, string RoleName, int? HierarchyLevel, string RoleType);
public record UpdateRoleRequest(string RoleName, int? HierarchyLevel, bool IsActive);

public record CreateEmployeeRequest(
    string EmployeeCode, string FullName, string Email, string Password, int RoleId, int? ManagerEmployeeId);

public record UpdateEmployeeRequest(string FullName, int RoleId, int? ManagerEmployeeId, bool IsActive);

public record EmployeeListItemDto(
    int EmployeeId, string EmployeeCode, string FullName, string Email,
    string RoleCode, string RoleName, string? ChannelName, string? ManagerName, bool IsActive);
