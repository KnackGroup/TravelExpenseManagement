namespace TravelExpense.Api.Data.Entities;

public class Employee : ICreationTimestamped, IUpdateTimestamped
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public int RoleId { get; set; }
    public int? ManagerEmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Role Role { get; set; } = default!;
    public Employee? Manager { get; set; }
    public ICollection<Employee> DirectReports { get; set; } = new List<Employee>();

    void ICreationTimestamped.StampCreated(DateTime utcNow) => CreatedAt = utcNow;
    void IUpdateTimestamped.StampUpdated(DateTime utcNow) => UpdatedAt = utcNow;
}
