using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TradingBotVIPv3.Config;
using TradingBotVIPv3.Data;
using TradingBotVIPv3.Data.Models;

namespace TradingBotVIPv3.Jobs;

/// <summary>
/// Har 5 daqiqada ishlaydi:
///  1) Muddati tugagan → kanaldan chiqarish + xabar
///  2) N soat qolgan  → eslatma + uzaytirish tugmasi
///  3) Har kuni soat 09:00 da → admin hisoboti
/// </summary>
public sealed class ExpireSubscriptionsJob : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<ExpireSubscriptionsJob> _log;

    public ExpireSubscriptionsJob(IServiceProvider sp, ILogger<ExpireSubscriptionsJob> log)
    {
        _sp = sp;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("✅ ExpireSubscriptionsJob ishga tushdi");

        // Birinchi ishga tushishda 30 soniya kutamiz (DB tayyor bo'lishi uchun)
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnce(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "Job xatosi"); }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task RunOnce(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        var cfg = scope.ServiceProvider.GetRequiredService<BotConfig>();

        var now = DateTime.UtcNow;
        var notifyThreshold = now.AddHours(cfg.NotifyHoursBefore);
        var changed = false;

        // ── 1) Muddati tugaganlarni o'chirish ──────────────────────────────
        var expired = await db.Subscriptions
            .Include(s => s.User)
            .Include(s => s.Plan).ThenInclude(p => p.VipChannel)
            .Where(s => s.IsActive && s.ExpireDate <= now)
            .ToListAsync(ct);

        foreach (var sub in expired)
        {
            _log.LogInformation("Obuna #{Id} tugadi — {Name}", sub.Id, sub.User.FullName);
            sub.IsActive = false;
            changed = true;

            // Kanaldan chiqarish
            try
            {
                await bot.BanChatMember(
                    sub.Plan.VipChannel.TelegramChannelId,
                    sub.User.TelegramId,
                    cancellationToken: ct);

                await Task.Delay(600, ct);

                await bot.UnbanChatMember(
                    sub.Plan.VipChannel.TelegramChannelId,
                    sub.User.TelegramId,
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Kanaldan chiqarish xatosi #{Id}: {Msg}", sub.Id, ex.Message);
            }

            // Foydalanuvchiga xabar
            try
            {
                await bot.SendMessage(sub.User.TelegramId,
                    $"⏰ <b>VIP Obunangiz tugadi</b>\n\n" +
                    $"⭐️ Reja:   {sub.Plan.Title}\n" +
                    $"📺 Kanal:  {sub.Plan.VipChannel.Title}\n\n" +
                    "Yangi obuna olib, VIP kanaldan foydalanishda davom eting 👇",
                    parseMode: ParseMode.Html,
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("⭐️ Obuna olish", "plans_menu") }
                    }),
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Xabar yuborish xatosi #{Id}: {Msg}", sub.Id, ex.Message);
            }
        }

        // ── 2) Tez orada tugaydigan obunalarga eslatma ─────────────────────
        var aboutToExpire = await db.Subscriptions
            .Include(s => s.User)
            .Include(s => s.Plan)
            .Where(s => s.IsActive
                     && !s.Notified
                     && s.ExpireDate > now
                     && s.ExpireDate <= notifyThreshold)
            .ToListAsync(ct);

        foreach (var sub in aboutToExpire)
        {
            sub.Notified = true;
            changed = true;

            var timeLeft = sub.ExpireDate - now;
            var hoursLeft = (int)timeLeft.TotalHours;
            var minutesLeft = timeLeft.Minutes;

            try
            {
                await bot.SendMessage(sub.User.TelegramId,
                    $"⚠️ <b>Diqqat!</b> VIP Obunangiz tugayapti!\n\n" +
                    $"⭐️ Reja:   {sub.Plan.Title}\n" +
                    $"⏰ Qoldi:  <b>{hoursLeft} soat {minutesLeft} daqiqa</b>\n\n" +
                    "Uzilmaslik uchun hoziroq yangilang 👇",
                    parseMode: ParseMode.Html,
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("🔄 Hozir uzaytirish", "plans_menu") }
                    }),
                    cancellationToken: ct);

                _log.LogInformation("Eslatma yuborildi #{Id} — {Name}", sub.Id, sub.User.FullName);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Eslatma xatosi #{Id}: {Msg}", sub.Id, ex.Message);
            }
        }

        // ── 3) Har kuni 09:00 da admin hisoboti ───────────────────────────
        var localNow = DateTime.Now;
        var isReportTime = localNow.Hour == 9 && localNow.Minute < 5;

        if (isReportTime)
        {
            var today = DateTime.UtcNow.Date;
            var todayIncome = await db.Payments
                .Where(p => p.Status == PaymentStatus.Approved && p.CreatedAt >= today)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
            var newUsers = await db.Users.CountAsync(u => u.CreatedAt >= today, ct);
            var activeVip = await db.Subscriptions.CountAsync(s => s.IsActive && s.ExpireDate > DateTime.UtcNow, ct);

            try
            {
                await bot.SendMessage(cfg.AdminId,
                    $"📊 <b>Kunlik Hisobot — {localNow:dd.MM.yyyy}</b>\n\n" +
                    $"👥 Yangi foydalanuvchi: <b>{newUsers}</b>\n" +
                    $"⭐️ Faol VIP:           <b>{activeVip}</b>\n" +
                    $"💰 Bugungi daromad:     <b>{todayIncome:N0} UZS</b>",
                    parseMode: ParseMode.Html, cancellationToken: ct);
            }
            catch { /* ignore */ }
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }
}