namespace TravelExpense.Api.Data.Entities;

public class Channel : ICreationTimestamped
{
    public int ChannelId { get; set; }
    public string ChannelCode { get; set; } = default!;
    public string ChannelName { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ICollection<Role> Roles { get; set; } = new List<Role>();

    void ICreationTimestamped.StampCreated(DateTime utcNow) => CreatedAt = utcNow;
}
