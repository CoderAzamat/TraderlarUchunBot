using TradingBotVIPv3.Data.Models;

namespace TradingBotVIPv3.Data.Models;

public enum PaymentStatus { Pending = 0, Approved = 1, Rejected = 2 }
public enum PaymentType { TopUp = 0, Subscription = 1 }

public class Payment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? PlanId { get; set; }
    public decimal Amount { get; set; }
    public string ReceiptFileId { get; set; } = string.Empty;
    public int? AdminMessageId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public PaymentType Type { get; set; } = PaymentType.Subscription;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Plan? Plan { get; set; }
}