using TradingBotVIPv3.Data.Models;

namespace TradingBotVIPv3.Data.Models;

public class Subscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int PlanId { get; set; }
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime ExpireDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool Notified { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Plan Plan { get; set; } = null!;
}