using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Data;
using TravelExpense.Api.Data.Entities;

namespace TravelExpense.Api.Services;

/// <summary>
/// Resolves who should act next in a workflow, based purely on the dynamic hierarchy
/// (Employee.ManagerEmployeeId chain + Role.RoleType), so re-orgs never require code changes.
/// </summary>
public class ApprovalRoutingService
{
    private readonly AppDbContext _db;

    public ApprovalRoutingService(AppDbContext db) => _db = db;

    /// <summary>The requester's immediate reporting manager - the "Superior" approval stage
    /// for advances and the reporting authority who must clear expense-policy exceptions.
    /// Returns null if the requester has no manager (e.g. a GM at the top of the chain).</summary>
    public async Task<Employee?> GetSuperiorAsync(int employeeId)
    {
        var employee = await _db.Employees.Include(e => e.Manager).ThenInclude(m => m!.Role)
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
        return employee?.Manager;
    }

    /// <summary>Any active employee whose role is of type Accounts - the finance approval
    /// and disbursement stage.</summary>
    public async Task<List<Employee>> GetAccountsApproversAsync()
    {
        return await _db.Employees
            .Include(e => e.Role)
            .Where(e => e.IsActive && e.Role.RoleType == RoleType.Accounts)
            .ToListAsync();
    }

    public async Task<bool> IsAccountsUserAsync(int employeeId)
    {
        var role = await _db.Employees.Where(e => e.EmployeeId == employeeId).Select(e => e.Role.RoleType).FirstOrDefaultAsync();
        return role == RoleType.Accounts;
    }

    public async Task<bool> IsAdminUserAsync(int employeeId)
    {
        var role = await _db.Employees.Where(e => e.EmployeeId == employeeId).Select(e => e.Role.RoleType).FirstOrDefaultAsync();
        return role == RoleType.Admin;
    }

    /// <summary>True if candidateManagerId is targetEmployeeId's manager, grand-manager, etc.
    /// (i.e. targetEmployeeId reports up to candidateManagerId somewhere in the chain).
    /// Used to gate a manager's access to a subordinate's tour plans/records.</summary>
    public async Task<bool> IsInReportingChainAsync(int targetEmployeeId, int candidateManagerId)
    {
        var currentId = (int?)targetEmployeeId;
        for (var depth = 0; depth < 20 && currentId.HasValue; depth++)
        {
            var managerId = await _db.Employees.Where(e => e.EmployeeId == currentId).Select(e => e.ManagerEmployeeId).FirstOrDefaultAsync();
            if (managerId == candidateManagerId) return true;
            currentId = managerId;
        }
        return false;
    }
}
