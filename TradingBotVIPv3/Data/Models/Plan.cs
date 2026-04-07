using TradingBotVIPv3.Data.Models;

namespace TradingBotVIPv3.Data.Models;

public class Plan
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public decimal Price { get; set; }
    public int VipChannelId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public VipChannel VipChannel { get; set; } = null!;
    public List<Subscription> Subscriptions { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}