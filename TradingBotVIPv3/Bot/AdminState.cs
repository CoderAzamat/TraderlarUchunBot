namespace TradingBotVIPv3.Bot;

public sealed class AdminStateService
{
    private readonly Dictionary<long, AdminSession> _sessions = new();

    public bool IsAdmin(long telegramId, TradingBotVIPv3.Config.BotConfig cfg,
                        TradingBotVIPv3.Data.AppDbContext db)
    {
        if (telegramId == cfg.AdminId) return true;
        return db.AdminUsers.Any(a => a.TelegramId == telegramId);
    }

    public AdminSession Get(long adminId)
    {
        if (!_sessions.TryGetValue(adminId, out var s))
        { s = new AdminSession(); _sessions[adminId] = s; }
        return s;
    }

    public void Clear(long adminId)
    {
        if (_sessions.TryGetValue(adminId, out var s)) s.Reset();
    }
}

public sealed class AdminSession
{
    public AdminStep Step { get; set; } = AdminStep.None;
    public int? EditChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public int? EditPlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public int PlanDays { get; set; }
    public decimal PlanPrice { get; set; }
    public int PlanChannelId { get; set; }
    public int TargetUserId { get; set; }
    public int TargetPaymentId { get; set; }
    public string EditSettingKey { get; set; } = string.Empty;
    public int? EditTrChId { get; set; }
    public string TrChTitle { get; set; } = string.Empty;
    public string TrChDesc { get; set; } = string.Empty;
    public string TrChLink { get; set; } = string.Empty;
    public string TrChEmoji { get; set; } = "📢";

    public void Reset()
    {
        Step = AdminStep.None;
        EditChannelId = null; EditPlanId = null; EditTrChId = null;
        ChannelName = string.Empty; PlanName = string.Empty;
        PlanDays = 0; PlanPrice = 0; PlanChannelId = 0;
        TargetUserId = 0; TargetPaymentId = 0;
        EditSettingKey = string.Empty;
        TrChTitle = string.Empty; TrChDesc = string.Empty;
        TrChLink = string.Empty; TrChEmoji = "📢";
    }
}

public enum AdminStep
{
    None,
    WaitChannelName, WaitChannelId,
    WaitPlanName, WaitPlanDays, WaitPlanPrice, WaitPlanChannel,
    WaitBalanceUserId, WaitBalanceAmount,
    WaitRejectReason,
    WaitBroadcastText,
    WaitUserSearch,
    WaitUserMessageText,
    WaitSettingValue,
    WaitMandatoryChannelId,
    WaitNewAdminId,
    WaitTrChTitle, WaitTrChDesc, WaitTrChLink, WaitTrChEmoji,
}