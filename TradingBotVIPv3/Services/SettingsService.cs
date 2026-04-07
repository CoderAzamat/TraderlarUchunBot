using Microsoft.EntityFrameworkCore;
using TradingBotVIPv3.Data;
using TradingBotVIPv3.Data.Models;

namespace TradingBotVIPv3.Services;

public sealed class SettingsService
{
    private readonly AppDbContext _db;

    public const string CardNumber = "CardNumber";
    public const string CardOwner = "CardOwner";
    public const string SupportUsername = "SupportUsername";
    public const string WelcomeMessage = "WelcomeMessage";
    public const string NotifyHoursBefore = "NotifyHoursBefore";
    public const string SmsEnabled = "SmsEnabled";
    public const string MandatoryEnabled = "MandatoryEnabled";
    public const string MandatoryChannels = "MandatoryChannels";
    public const string BotAboutText = "BotAboutText";
    public const string WebAdminUrl = "WebAdminUrl";

    public SettingsService(AppDbContext db) => _db = db;

    public async Task<string> Get(string key, string defaultValue = "", CancellationToken ct = default)
    {
        var s = await _db.Settings.FirstOrDefaultAsync(x => x.Key == key, ct);
        return s?.Value ?? defaultValue;
    }

    public async Task Set(string key, string value, CancellationToken ct = default)
    {
        var s = await _db.Settings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (s is null) _db.Settings.Add(new Setting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
        else { s.Value = value; s.UpdatedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> GetBool(string key, bool def = false, CancellationToken ct = default)
    {
        var v = await Get(key, def.ToString(), ct);
        return bool.TryParse(v, out var r) ? r : def;
    }

    public async Task<int> GetInt(string key, int def = 0, CancellationToken ct = default)
    {
        var v = await Get(key, def.ToString(), ct);
        return int.TryParse(v, out var r) ? r : def;
    }

    public async Task<List<long>> GetMandatoryChannelIds(CancellationToken ct = default)
    {
        var v = await Get(MandatoryChannels, "", ct);
        if (string.IsNullOrWhiteSpace(v)) return new();
        return v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => long.TryParse(x.Trim(), out var id) ? id : 0)
                .Where(x => x != 0).ToList();
    }

    public async Task InitDefaults(string cardNumber, string cardOwner, string support, string webhookUrl, CancellationToken ct = default)
    {
        await SetIfEmpty(CardNumber, cardNumber, ct);
        await SetIfEmpty(CardOwner, cardOwner, ct);
        await SetIfEmpty(SupportUsername, support, ct);
        await SetIfEmpty(WelcomeMessage, "👋 <b>VIP Bot ga xush kelibsiz!</b>\n\n📝 Ismingizni kiriting:", ct);
        await SetIfEmpty(BotAboutText, "Bu bot orqali VIP kanalga obuna bo'lishingiz mumkin.", ct);
        await SetIfEmpty(NotifyHoursBefore, "12", ct);
        await SetIfEmpty(SmsEnabled, "false", ct);
        await SetIfEmpty(MandatoryEnabled, "false", ct);
        await SetIfEmpty(MandatoryChannels, "", ct);
        // Web admin URL — webhook URL dan olamiz
        var baseUrl = webhookUrl.Replace("/api/bot/update", "");
        await SetIfEmpty(WebAdminUrl, $"{baseUrl}/admin", ct);
    }

    private async Task SetIfEmpty(string key, string value, CancellationToken ct)
    {
        var existing = await _db.Settings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (existing is null) _db.Settings.Add(new Setting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(ct);
    }
}