namespace TradingBotVIPv3.Data.Models;

public class AdminUser
{
    public int Id { get; set; }
    public long TelegramId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool IsSuper { get; set; } = false; // Super admin — hammani o'zgartira oladi
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}