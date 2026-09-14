namespace TravelExpense.Api.Data.Entities;

public static class RoleType
{
    public const string Sales = "Sales";
    public const string Accounts = "Accounts";
    public const string Admin = "Admin";
}

public class Role : ICreationTimestamped
{
    public int RoleId { get; set; }
    public int? ChannelId { get; set; }
    public string RoleCode { get; set; } = default!;
    public string RoleName { get; set; } = default!;
    public int? HierarchyLevel { get; set; }
    public string RoleType { get; set; } = Entities.RoleType.Sales;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Channel? Channel { get; set; }
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    public ICollection<PolicyCap> PolicyCaps { get; set; } = new List<PolicyCap>();

    void ICreationTimestamped.StampCreated(DateTime utcNow) => CreatedAt = utcNow;
}
