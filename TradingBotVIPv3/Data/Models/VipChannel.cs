namespace TradingBotVIPv3.Data.Models;

public class VipChannel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public long TelegramChannelId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Plan> Plans { get; set; } = [];
}