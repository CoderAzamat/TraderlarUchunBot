using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using TradingBotVIPv3.Services;
using TradingBotVIPv3.Config;
using TradingBotVIPv3.Data;
using TradingBotVIPv3.Data.Models;
using TradingBotVIPv3.Helpers;

namespace TradingBotVIPv3.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITelegramBotClient _bot;
    private readonly BotConfig _cfg;
    private readonly PdfExportService _pdf;

    public AdminApiController(AppDbContext db, ITelegramBotClient bot, BotConfig cfg, PdfExportService pdf)
    { _db = db; _bot = bot; _cfg = cfg; _pdf = pdf; }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var thisMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var totalUsers = await _db.Users.CountAsync(ct);
        var todayUsers = await _db.Users.CountAsync(u => u.CreatedAt >= today, ct);
        var activeVip = await _db.Subscriptions.CountAsync(s => s.IsActive && s.ExpireDate > DateTime.UtcNow, ct);
        var pendingPay = await _db.Payments.CountAsync(p => p.Status == PaymentStatus.Pending, ct);
        var todayIncome = await _db.Payments.Where(p => p.Status == PaymentStatus.Approved && p.CreatedAt >= today).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var monthIncome = await _db.Payments.Where(p => p.Status == PaymentStatus.Approved && p.CreatedAt >= thisMonth).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var totalIncome = await _db.Payments.Where(p => p.Status == PaymentStatus.Approved).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var last7 = Enumerable.Range(0, 7).Select(i => today.AddDays(-i)).ToList();
        var inc7 = await _db.Payments.Where(p => p.Status == PaymentStatus.Approved && p.CreatedAt >= today.AddDays(-6))
            .GroupBy(p => p.CreatedAt.Date).Select(g => new { Date = g.Key, Amount = g.Sum(p => p.Amount) }).ToListAsync(ct);
        var chartData = last7.OrderBy(d => d).Select(d => new { date = d.TashkentDate(), amount = inc7.FirstOrDefault(x => x.Date == d)?.Amount ?? 0 }).ToList();
        var recentPayments = await _db.Payments.Include(p => p.User).Include(p => p.Plan).OrderByDescending(p => p.CreatedAt).Take(5)
            .Select(p => new { p.Id, userName = p.User.FullName, userPhone = p.User.PhoneNumber, planTitle = p.Plan != null ? p.Plan.Title : "TopUp", p.Amount, p.Status, createdAt = p.CreatedAt.TashkentFull() }).ToListAsync(ct);
        return Ok(new { totalUsers, todayUsers, activeVip, pendingPayments = pendingPay, todayIncome, monthIncome, totalIncome, chartData, recentPayments });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var q = _db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(u => u.FullName.Contains(search) || u.PhoneNumber.Contains(search) || u.TelegramId.ToString().Contains(search) || u.Id.ToString() == search);
        var total = await q.CountAsync(ct);
        var users = await q.OrderByDescending(u => u.CreatedAt).Skip((page - 1) * limit).Take(limit)
            .Select(u => new {
                u.Id,
                u.TelegramId,
                u.FullName,
                u.PhoneNumber,
                u.Balance,
                u.IsAdmin,
                u.IsBanned,
                u.UserStep,
                createdAt = u.CreatedAt.TashkentFull(),
                hasActiveSub = u.Subscriptions.Any(s => s.IsActive && s.ExpireDate > DateTime.UtcNow)
            }).ToListAsync(ct);
        return Ok(new { total, page, limit, users });
    }

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUser(int id, CancellationToken ct)
    {
        var u = await _db.Users.Include(u => u.Subscriptions).ThenInclude(s => s.Plan).Include(u => u.Payments).ThenInclude(p => p.Plan).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (u is null) return NotFound();
        return Ok(new
        {
            u.Id,
            u.TelegramId,
            u.FullName,
            u.PhoneNumber,
            u.Balance,
            u.IsAdmin,
            u.IsBanned,
            createdAt = u.CreatedAt.TashkentFull(),
            subscriptions = u.Subscriptions.Select(s => new { s.Id, planTitle = s.Plan.Title, s.IsActive, startDate = s.StartDate.TashkentDate(), expireDate = s.ExpireDate.TashkentFull(), daysLeft = (int)(s.ExpireDate - DateTime.UtcNow).TotalDays }),
            payments = u.Payments.OrderByDescending(p => p.CreatedAt).Take(20).Select(p => new { p.Id, planTitle = p.Plan?.Title ?? "TopUp", p.Amount, p.Status, p.Type, createdAt = p.CreatedAt.TashkentFull() })
        });
    }

    [HttpPost("users/{id}/balance")]
    public async Task<IActionResult> AddBalance(int id, [FromBody] BalanceRequest req, CancellationToken ct)
    {
        var u = await _db.Users.FindAsync(new object[] { id }, ct); if (u is null) return NotFound();
        u.Balance += req.Amount; await _db.SaveChangesAsync(ct);
        try { await _bot.SendMessage(u.TelegramId, $"💰 <b>Balans yangilandi</b>\n\n{(req.Amount >= 0 ? "+" : "")}{req.Amount:N0} UZS\nBalans: <b>{u.Balance:N0} UZS</b>\nSabab: {req.Note ?? "Admin"}\n{TimeHelper.NowTashkent():dd.MM.yyyy HH:mm:ss}", parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, cancellationToken: ct); } catch { }
        return Ok(new { u.Balance });
    }

    [HttpPost("users/{id}/ban")]
    public async Task<IActionResult> BanUser(int id, [FromBody] BanRequest req, CancellationToken ct)
    {
        var u = await _db.Users.FindAsync(new object[] { id }, ct); if (u is null) return NotFound();
        u.IsBanned = req.Ban; await _db.SaveChangesAsync(ct);
        try { await _bot.SendMessage(u.TelegramId, req.Ban ? $"🚫 <b>Hisobingiz bloklandi.</b>\n{_cfg.SupportUsername}" : "✅ <b>Hisobingiz faollashtirildi.</b>", parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, cancellationToken: ct); } catch { }
        return Ok(new { u.IsBanned });
    }

    [HttpPost("users/{id}/message")]
    public async Task<IActionResult> SendMessage(int id, [FromBody] MessageRequest req, CancellationToken ct)
    {
        var u = await _db.Users.FindAsync(new object[] { id }, ct); if (u is null) return NotFound();
        try { await _bot.SendMessage(u.TelegramId, $"📢 <b>Admin xabari:</b>\n\n{req.Text}", parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, cancellationToken: ct); }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
        return Ok();
    }

    [HttpGet("payments")]
    public async Task<IActionResult> GetPayments([FromQuery] PaymentStatus? status, [FromQuery] int page = 1, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var q = _db.Payments.Include(p => p.User).Include(p => p.Plan).AsQueryable();
        if (status.HasValue) q = q.Where(p => p.Status == status.Value);
        var total = await q.CountAsync(ct);
        var payments = await q.OrderByDescending(p => p.CreatedAt).Skip((page - 1) * limit).Take(limit)
            .Select(p => new { p.Id, userName = p.User.FullName, userPhone = p.User.PhoneNumber, userId = p.UserId, planTitle = p.Plan != null ? p.Plan.Title : "Hisob to'ldirish", p.Amount, p.Status, p.Type, p.ReceiptFileId, createdAt = p.CreatedAt.TashkentFull() }).ToListAsync(ct);
        return Ok(new { total, page, limit, payments });
    }

    [HttpPost("payments/{id}/approve")]
    public async Task<IActionResult> ApprovePayment(int id, CancellationToken ct)
    {
        var p = await _db.Payments.Include(p => p.User).Include(p => p.Plan).ThenInclude(p => p!.VipChannel).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (p.Status != PaymentStatus.Pending) return BadRequest(new { error = "Allaqachon ko'rib chiqilgan" });
        p.Status = PaymentStatus.Approved;
        if (p.Type == PaymentType.TopUp) { p.User.Balance += p.Amount; await _db.SaveChangesAsync(ct); await _bot.SendMessage(p.User.TelegramId, $"✅ <b>Hisobingiz to'ldirildi!</b>\n\n+{p.Amount:N0} UZS\nBalans: <b>{p.User.Balance:N0} UZS</b>\n{TimeHelper.NowTashkent():dd.MM.yyyy HH:mm:ss}", parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, cancellationToken: ct); }
        else if (p.Plan is not null) { await _db.SaveChangesAsync(ct); var exp = await CreateOrExtendSub(p.UserId, p.PlanId!.Value, p.Plan.DurationDays, ct); string inv; try { var lnk = await _bot.CreateChatInviteLink(p.Plan.VipChannel.TelegramChannelId, memberLimit: 1, expireDate: DateTime.UtcNow.AddDays(1), cancellationToken: ct); inv = $"\n\n🔗 <b>VIP havola:</b>\n{lnk.InviteLink}\n\n⚠️ 1 marta ishlaydi!"; } catch { inv = ""; } await _bot.SendMessage(p.User.TelegramId, $"✅ <b>VIP Tasdiqlandi!</b>\n\n⭐️ {p.Plan.Title}\n📅 {exp.TashkentDate()} gacha" + inv, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, cancellationToken: ct); }
        return Ok(new { message = "Tasdiqlandi" });
    }

    [HttpPost("payments/{id}/reject")]
    public async Task<IActionResult> RejectPayment(int id, [FromBody] RejectRequest req, CancellationToken ct)
    {
        var p = await _db.Payments.Include(p => p.User).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (p.Status != PaymentStatus.Pending) return BadRequest(new { error = "Allaqachon ko'rib chiqilgan" });
        p.Status = PaymentStatus.Rejected; await _db.SaveChangesAsync(ct);
        await _bot.SendMessage(p.User.TelegramId, $"❌ <b>To'lovingiz rad etildi.</b>\n\nSabab: {req.Reason ?? "Chek tasdiqlanmadi"}\n\n📞 {_cfg.SupportUsername}", parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, cancellationToken: ct);
        return Ok();
    }

    [HttpGet("channels")]
    public async Task<IActionResult> GetChannels(CancellationToken ct) =>
        Ok(await _db.VipChannels.Include(v => v.Plans).OrderBy(v => v.Id).Select(v => new { v.Id, v.Title, v.TelegramChannelId, v.IsActive, createdAt = v.CreatedAt.TashkentDate(), plansCount = v.Plans.Count(p => p.IsActive) }).ToListAsync(ct));

    [HttpPost("channels")]
    public async Task<IActionResult> CreateChannel([FromBody] ChannelRequest req, CancellationToken ct)
    { var ch = new VipChannel { Title = req.Title, TelegramChannelId = req.TelegramChannelId, IsActive = true }; _db.VipChannels.Add(ch); await _db.SaveChangesAsync(ct); return Ok(ch); }

    [HttpPut("channels/{id}")]
    public async Task<IActionResult> UpdateChannel(int id, [FromBody] ChannelRequest req, CancellationToken ct)
    { var ch = await _db.VipChannels.FindAsync(new object[] { id }, ct); if (ch is null) return NotFound(); ch.Title = req.Title; ch.TelegramChannelId = req.TelegramChannelId; ch.IsActive = req.IsActive; await _db.SaveChangesAsync(ct); return Ok(ch); }

    [HttpDelete("channels/{id}")]
    public async Task<IActionResult> DeleteChannel(int id, CancellationToken ct)
    { var ch = await _db.VipChannels.FindAsync(new object[] { id }, ct); if (ch is null) return NotFound(); if (await _db.Plans.AnyAsync(p => p.VipChannelId == id, ct)) return BadRequest(new { error = "Kanalda rejalar bor." }); _db.VipChannels.Remove(ch); await _db.SaveChangesAsync(ct); return Ok(); }

    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans(CancellationToken ct) =>
        Ok(await _db.Plans.Include(p => p.VipChannel).OrderBy(p => p.VipChannelId).ThenBy(p => p.Price).Select(p => new { p.Id, p.Title, p.DurationDays, p.Price, p.VipChannelId, channelTitle = p.VipChannel.Title, p.IsActive, createdAt = p.CreatedAt.TashkentDate(), soldCount = p.Subscriptions.Count }).ToListAsync(ct));

    [HttpPost("plans")]
    public async Task<IActionResult> CreatePlan([FromBody] PlanRequest req, CancellationToken ct)
    { var p = new Plan { Title = req.Title, DurationDays = req.DurationDays, Price = req.Price, VipChannelId = req.VipChannelId, IsActive = true }; _db.Plans.Add(p); await _db.SaveChangesAsync(ct); return Ok(p); }

    [HttpPut("plans/{id}")]
    public async Task<IActionResult> UpdatePlan(int id, [FromBody] PlanRequest req, CancellationToken ct)
    { var p = await _db.Plans.FindAsync(new object[] { id }, ct); if (p is null) return NotFound(); p.Title = req.Title; p.DurationDays = req.DurationDays; p.Price = req.Price; p.VipChannelId = req.VipChannelId; p.IsActive = req.IsActive; await _db.SaveChangesAsync(ct); return Ok(p); }

    [HttpDelete("plans/{id}")]
    public async Task<IActionResult> DeletePlan(int id, CancellationToken ct)
    { var p = await _db.Plans.FindAsync(new object[] { id }, ct); if (p is null) return NotFound(); p.IsActive = false; await _db.SaveChangesAsync(ct); return Ok(); }

    [HttpGet("subscriptions")]
    public async Task<IActionResult> GetSubscriptions([FromQuery] bool? active, [FromQuery] int page = 1, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var q = _db.Subscriptions.Include(s => s.User).Include(s => s.Plan).AsQueryable();
        if (active.HasValue) q = q.Where(s => s.IsActive == active.Value);
        var total = await q.CountAsync(ct);
        var subs = await q.OrderByDescending(s => s.ExpireDate).Skip((page - 1) * limit).Take(limit)
            .Select(s => new { s.Id, userName = s.User.FullName, userPhone = s.User.PhoneNumber, planTitle = s.Plan.Title, startDate = s.StartDate.TashkentDate(), expireDate = s.ExpireDate.TashkentFull(), s.IsActive, daysLeft = (int)(s.ExpireDate - DateTime.UtcNow).TotalDays }).ToListAsync(ct);
        return Ok(new { total, page, limit, subs });
    }

    [HttpPost("broadcast")]
    public async Task<IActionResult> Broadcast([FromBody] BroadcastRequest req, CancellationToken ct)
    {
        var ids = await _db.Users.Where(u => !u.IsBanned && u.UserStep == "Active").Select(u => u.TelegramId).ToListAsync(ct);
        int sent = 0, failed = 0;
        foreach (var id in ids) { try { await _bot.SendMessage(id, $"📢 <b>Xabar</b>\n\n{req.Text}", parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, cancellationToken: ct); sent++; await Task.Delay(50, ct); } catch { failed++; } }
        return Ok(new { total = ids.Count, sent, failed });
    }

    [HttpGet("settings")]
    public IActionResult GetSettings() => Ok(new { _cfg.CardNumber, _cfg.CardOwner, _cfg.SupportUsername, _cfg.NotifyHoursBefore, _cfg.SmsEnabled, _cfg.WebhookUrl, serverTime = TimeHelper.NowTashkent().ToString("dd.MM.yyyy HH:mm:ss") });

    [HttpGet("export/users")]
    public async Task<IActionResult> ExportUsers(CancellationToken ct)
    { var html = await _pdf.GenerateUsersPdfAsync(ct); return File(html, "text/html; charset=utf-8", $"users_{TimeHelper.NowTashkent():yyyyMMdd_HHmmss}.html"); }

    [HttpGet("export/payments")]
    public async Task<IActionResult> ExportPayments(CancellationToken ct)
    { var html = await _pdf.GeneratePaymentsPdfAsync(ct); return File(html, "text/html; charset=utf-8", $"payments_{TimeHelper.NowTashkent():yyyyMMdd_HHmmss}.html"); }

    private async Task<DateTime> CreateOrExtendSub(int userId, int planId, int days, CancellationToken ct)
    {
        var ex = await _db.Subscriptions.Where(s => s.UserId == userId && s.IsActive).FirstOrDefaultAsync(ct);
        if (ex is not null && ex.ExpireDate > DateTime.UtcNow) { ex.ExpireDate = ex.ExpireDate.AddDays(days); ex.Notified = false; await _db.SaveChangesAsync(ct); return ex.ExpireDate; }
        var sub = new Subscription { UserId = userId, PlanId = planId, StartDate = DateTime.UtcNow, ExpireDate = DateTime.UtcNow.AddDays(days), IsActive = true };
        _db.Subscriptions.Add(sub); await _db.SaveChangesAsync(ct); return sub.ExpireDate;
    }
}

public record BalanceRequest(decimal Amount, string? Note);
public record BanRequest(bool Ban);
public record MessageRequest(string Text);
public record RejectRequest(string? Reason);
public record ChannelRequest(string Title, long TelegramChannelId, bool IsActive = true);
public record PlanRequest(string Title, int DurationDays, decimal Price, int VipChannelId, bool IsActive = true);
public record BroadcastRequest(string Text);