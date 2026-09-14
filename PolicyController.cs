using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Auth;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;
using TravelExpense.Api.Dtos;

namespace TravelExpense.Api.Controllers;

/// <summary>
/// The travel policy itself: expense categories and, per role, the max-per-day / max-per-
/// booking caps and flight/train eligibility. This is what makes "Area Manager can't book
/// flights" or "General Manager gets a higher hotel cap" a data change, not a code change.
/// </summary>
[ApiController]
[Route("api/policy")]
[Authorize]
public class PolicyController : ControllerBase
{
    private readonly AppDbContext _db;

    public PolicyController(AppDbContext db) => _db = db;

    [HttpGet("categories")]
    public async Task<ActionResult<List<ExpenseCategoryDto>>> GetCategories()
    {
        var categories = await _db.ExpenseCategories.Where(c => c.IsActive)
            .Select(c => new ExpenseCategoryDto(c.ExpenseCategoryId, c.CategoryCode, c.CategoryName, c.IsTicketCategory))
            .ToListAsync();
        return Ok(categories);
    }

    [HttpGet("caps")]
    public async Task<ActionResult<List<PolicyCapDto>>> GetCaps([FromQuery] int? roleId, [FromQuery] DateOnly? asOf)
    {
        var onDate = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var query = _db.PolicyCaps
            .Include(p => p.Role).ThenInclude(r => r.Channel)
            .Include(p => p.ExpenseCategory)
            .Where(p => p.EffectiveFrom <= onDate && (p.EffectiveTo == null || p.EffectiveTo >= onDate));

        if (roleId.HasValue) query = query.Where(p => p.RoleId == roleId);

        var caps = await query
            .Select(p => new PolicyCapDto(
                p.PolicyCapId, p.RoleId, p.Role.RoleCode, p.Role.RoleName, p.Role.Channel != null ? p.Role.Channel.ChannelName : null,
                p.ExpenseCategoryId, p.ExpenseCategory.CategoryCode, p.ExpenseCategory.CategoryName,
                p.IsAllowed, p.MaxAmountPerDay, p.MaxAmountPerBooking, p.MaxClass,
                p.Currency, p.EffectiveFrom, p.EffectiveTo))
            .ToListAsync();
        return Ok(caps);
    }

    /// <summary>Full policy matrix for a role, in one call - what the expense entry screen
    /// pre-loads so it can highlight overages client-side before submit.</summary>
    [HttpGet("matrix/{roleId:int}")]
    public async Task<ActionResult<RolePolicyMatrixDto>> GetMatrix(int roleId)
    {
        var role = await _db.Roles.FindAsync(roleId);
        if (role == null) return NotFound();

        var onDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var caps = await _db.PolicyCaps
            .Include(p => p.Role).ThenInclude(r => r.Channel)
            .Include(p => p.ExpenseCategory)
            .Where(p => p.RoleId == roleId && p.EffectiveFrom <= onDate && (p.EffectiveTo == null || p.EffectiveTo >= onDate))
            .Select(p => new PolicyCapDto(
                p.PolicyCapId, p.RoleId, p.Role.RoleCode, p.Role.RoleName, p.Role.Channel != null ? p.Role.Channel.ChannelName : null,
                p.ExpenseCategoryId, p.ExpenseCategory.CategoryCode, p.ExpenseCategory.CategoryName,
                p.IsAllowed, p.MaxAmountPerDay, p.MaxAmountPerBooking, p.MaxClass,
                p.Currency, p.EffectiveFrom, p.EffectiveTo))
            .ToListAsync();

        return Ok(new RolePolicyMatrixDto(role.RoleId, role.RoleCode, role.RoleName, caps));
    }

    [HttpPost("caps")]
    [Authorize(Roles = RoleType.Admin)]
    public async Task<ActionResult> UpsertCap(UpsertPolicyCapRequest request)
    {
        var employeeId = User.GetEmployeeId();

        // Close out any currently-open cap for the same role+category so history is preserved
        // instead of overwritten (policy versioning).
        var existingOpen = await _db.PolicyCaps
            .Where(p => p.RoleId == request.RoleId && p.ExpenseCategoryId == request.ExpenseCategoryId && p.EffectiveTo == null)
            .ToListAsync();
        foreach (var old in existingOpen)
            old.EffectiveTo = request.EffectiveFrom.AddDays(-1);

        var cap = new PolicyCap
        {
            RoleId = request.RoleId,
            ExpenseCategoryId = request.ExpenseCategoryId,
            IsAllowed = request.IsAllowed,
            MaxAmountPerDay = request.MaxAmountPerDay,
            MaxAmountPerBooking = request.MaxAmountPerBooking,
            MaxClass = request.MaxClass,
            Currency = request.Currency,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            CreatedByEmployeeId = employeeId
        };
        _db.PolicyCaps.Add(cap);
        await _db.SaveChangesAsync();
        return Ok(new { cap.PolicyCapId });
    }
}
