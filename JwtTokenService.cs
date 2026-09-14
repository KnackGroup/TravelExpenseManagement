using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TravelExpense.Api.Data.Entities;

namespace TravelExpense.Api.Auth;

public class JwtOptions
{
    public string SigningKey { get; set; } = default!;
    public string Issuer { get; set; } = "TravelExpenseApi";
    public string Audience { get; set; } = "TravelExpenseClient";
    public int ExpiryMinutes { get; set; } = 480;
}

/// <summary>
/// Custom claim types used across the API for authorization:
///   employee_id, role_code, role_type, channel_id, hierarchy_level, manager_id
/// These are what [Authorize] policies and controllers read to enforce
/// hierarchy/channel-aware rules (e.g. "only Accounts role can disburse").
/// </summary>
public static class AppClaimTypes
{
    public const string EmployeeId = "employee_id";
    public const string RoleCode = "role_code";
    public const string RoleType = "role_type";
    public const string ChannelId = "channel_id";
    public const string HierarchyLevel = "hierarchy_level";
    public const string ManagerId = "manager_id";
}

public class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(JwtOptions options) => _options = options;

    public string GenerateToken(Employee employee)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, employee.EmployeeId.ToString()),
            new(JwtRegisteredClaimNames.Email, employee.Email),
            new(ClaimTypes.Name, employee.FullName),
            new(AppClaimTypes.EmployeeId, employee.EmployeeId.ToString()),
            new(AppClaimTypes.RoleCode, employee.Role.RoleCode),
            new(AppClaimTypes.RoleType, employee.Role.RoleType),
            new(ClaimTypes.Role, employee.Role.RoleType),
        };

        if (employee.Role.ChannelId.HasValue)
            claims.Add(new Claim(AppClaimTypes.ChannelId, employee.Role.ChannelId.Value.ToString()));
        if (employee.Role.HierarchyLevel.HasValue)
            claims.Add(new Claim(AppClaimTypes.HierarchyLevel, employee.Role.HierarchyLevel.Value.ToString()));
        if (employee.ManagerEmployeeId.HasValue)
            claims.Add(new Claim(AppClaimTypes.ManagerId, employee.ManagerEmployeeId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int GetEmployeeId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(AppClaimTypes.EmployeeId) ?? throw new InvalidOperationException("Missing employee_id claim"));

    public static string GetRoleCode(this ClaimsPrincipal user) =>
        user.FindFirstValue(AppClaimTypes.RoleCode) ?? string.Empty;

    public static string GetRoleType(this ClaimsPrincipal user) =>
        user.FindFirstValue(AppClaimTypes.RoleType) ?? string.Empty;
}
