namespace TradingBotVIPv3.Config;

public sealed class BotConfig
{
    public string Token { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public long AdminId { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string CardOwner { get; set; } = string.Empty;
    public string SupportUsername { get; set; } = "@support";
    public int NotifyHoursBefore { get; set; } = 12;
    public bool SmsEnabled { get; set; } = false;
}