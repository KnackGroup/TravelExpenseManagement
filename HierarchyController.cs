using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Auth;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;
using TravelExpense.Api.Dtos;

namespace TravelExpense.Api.Controllers;

/// <summary>
/// Admin-configurable dynamic hierarchy: channels, roles (with level + type), and employees
/// (with their reporting manager). Reads are open to any authenticated user (dropdowns, org
/// chart); writes are restricted to the Admin role type.
/// </summary>
[ApiController]
[Route("api/hierarchy")]
[Authorize]
public class HierarchyController : ControllerBase
{
    private readonly AppDbContext _db;

    public HierarchyController(AppDbContext db) => _db = db;

    // ---------- Channels ----------

    [HttpGet("channels")]
    public async Task<ActionResult<List<ChannelDto>>> GetChannels()
    {
        var channels = await _db.Channels
            .Select(c => new ChannelDto(c.ChannelId, c.ChannelCode, c.ChannelName, c.IsActive))
            .ToListAsync();
        return Ok(channels);
    }

    [HttpPost("channels")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<ActionResult<ChannelDto>> CreateChannel(CreateChannelRequest request)
    {
        var channel = new Channel { ChannelCode = request.ChannelCode, ChannelName = request.ChannelName };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();
        return Ok(new ChannelDto(channel.ChannelId, channel.ChannelCode, channel.ChannelName, channel.IsActive));
    }

    // ---------- Roles ----------

    [HttpGet("roles")]
    public async Task<ActionResult<List<RoleDto>>> GetRoles([FromQuery] int? channelId)
    {
        var query = _db.Roles.Include(r => r.Channel).Where(r => r.IsActive);
        if (channelId.HasValue) query = query.Where(r => r.ChannelId == channelId);

        var roles = await query
            .OrderBy(r => r.ChannelId).ThenBy(r => r.HierarchyLevel)
            .Select(r => new RoleDto(r.RoleId, r.ChannelId, r.Channel != null ? r.Channel.ChannelName : null,
                r.RoleCode, r.RoleName, r.HierarchyLevel, r.RoleType, r.IsActive))
            .ToListAsync();
        return Ok(roles);
    }

    [HttpPost("roles")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<ActionResult<RoleDto>> CreateRole(CreateRoleRequest request)
    {
        var role = new Role
        {
            ChannelId = request.ChannelId,
            RoleCode = request.RoleCode,
            RoleName = request.RoleName,
            HierarchyLevel = request.HierarchyLevel,
            RoleType = request.RoleType
        };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        return Ok(new RoleDto(role.RoleId, role.ChannelId, null, role.RoleCode, role.RoleName, role.HierarchyLevel, role.RoleType, role.IsActive));
    }

    [HttpPut("roles/{roleId:int}")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<IActionResult> UpdateRole(int roleId, UpdateRoleRequest request)
    {
        var role = await _db.Roles.FindAsync(roleId);
        if (role == null) return NotFound();

        role.RoleName = request.RoleName;
        role.HierarchyLevel = request.HierarchyLevel;
        role.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Employees ----------

    [HttpGet("employees")]
    public async Task<ActionResult<List<EmployeeListItemDto>>> GetEmployees()
    {
        var employees = await _db.Employees
            .Include(e => e.Role).ThenInclude(r => r.Channel)
            .Include(e => e.Manager)
            .OrderBy(e => e.Role.ChannelId).ThenBy(e => e.Role.HierarchyLevel)
            .Select(e => new EmployeeListItemDto(
                e.EmployeeId, e.EmployeeCode, e.FullName, e.Email,
                e.Role.RoleCode, e.Role.RoleName,
                e.Role.Channel != null ? e.Role.Channel.ChannelName : null,
                e.Manager != null ? e.Manager.FullName : null, e.IsActive))
            .ToListAsync();
        return Ok(employees);
    }

    /// <summary>Direct reports of a manager - used to build approval-inbox and org-chart views.</summary>
    [HttpGet("employees/{employeeId:int}/direct-reports")]
    public async Task<ActionResult<List<EmployeeListItemDto>>> GetDirectReports(int employeeId)
    {
        var reports = await _db.Employees
            .Include(e => e.Role).ThenInclude(r => r.Channel)
            .Include(e => e.Manager)
            .Where(e => e.ManagerEmployeeId == employeeId)
            .Select(e => new EmployeeListItemDto(
                e.EmployeeId, e.EmployeeCode, e.FullName, e.Email,
                e.Role.RoleCode, e.Role.RoleName,
                e.Role.Channel != null ? e.Role.Channel.ChannelName : null,
                e.Manager != null ? e.Manager.FullName : null, e.IsActive))
            .ToListAsync();
        return Ok(reports);
    }

    [HttpPost("employees")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<ActionResult> CreateEmployee(CreateEmployeeRequest request)
    {
        var employee = new Employee
        {
            EmployeeCode = request.EmployeeCode,
            FullName = request.FullName,
            Email = request.Email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            RoleId = request.RoleId,
            ManagerEmployeeId = request.ManagerEmployeeId
        };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        return Ok(new { employee.EmployeeId });
    }

    [HttpPut("employees/{employeeId:int}")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<IActionResult> UpdateEmployee(int employeeId, UpdateEmployeeRequest request)
    {
        var employee = await _db.Employees.FindAsync(employeeId);
        if (employee == null) return NotFound();

        employee.FullName = request.FullName;
        employee.RoleId = request.RoleId;
        employee.ManagerEmployeeId = request.ManagerEmployeeId;
        employee.IsActive = request.IsActive;
        employee.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
