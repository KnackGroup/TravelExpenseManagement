using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Auth;
using TravelExpense.Api.Data;
using TravelExpense.Api.Dtos;

namespace TravelExpense.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwt;

    public AuthController(AppDbContext db, JwtTokenService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var employee = await _db.Employees
            .Include(e => e.Role).ThenInclude(r => r.Channel)
            .Include(e => e.Manager)
            .FirstOrDefaultAsync(e => e.Email == request.Email && e.IsActive);

        if (employee == null || !PasswordHasher.Verify(request.Password, employee.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        var token = _jwt.GenerateToken(employee);

        var dto = new EmployeeSummaryDto(
            employee.EmployeeId, employee.EmployeeCode, employee.FullName, employee.Email,
            employee.RoleId, employee.Role.RoleCode, employee.Role.RoleName, employee.Role.RoleType,
            employee.Role.ChannelId, employee.Role.Channel?.ChannelName, employee.Role.HierarchyLevel,
            employee.ManagerEmployeeId, employee.Manager?.FullName);

        return Ok(new LoginResponse(token, dto));
    }
}
