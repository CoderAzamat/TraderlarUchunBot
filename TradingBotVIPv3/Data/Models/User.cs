namespace TradingBotVIPv3.Data.Models;

public class User
{
    public int Id { get; set; }
    public long TelegramId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public decimal Balance { get; set; } = 0m;
    public bool IsAdmin { get; set; } = false;
    public bool IsBanned { get; set; } = false;

    /// <summary>WaitingForName | WaitingForPhone | Active</summary>
    public string UserStep { get; set; } = "WaitingForName";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Subscription> Subscriptions { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}