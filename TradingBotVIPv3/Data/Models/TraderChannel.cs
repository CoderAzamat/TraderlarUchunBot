namespace TradingBotVIPv3.Data.Models;

public class TraderChannel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;   // "Asosiy kanal"
    public string Description { get; set; } = string.Empty;   // "Bepul signallar"
    public string Link { get; set; } = string.Empty;   // "https://t.me/..."
    public string Emoji { get; set; } = "📢";
    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}