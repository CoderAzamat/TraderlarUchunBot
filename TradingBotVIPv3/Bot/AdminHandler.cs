using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TradingBotVIPv3.Config;
using TradingBotVIPv3.Data;
using TradingBotVIPv3.Data.Models;
using TradingBotVIPv3.Helpers;
using TradingBotVIPv3.Services;
using TBUser = TradingBotVIPv3.Data.Models.User;

namespace TradingBotVIPv3.Bot;

public sealed class AdminHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly AppDbContext _db;
    private readonly BotConfig _cfg;
    private readonly AdminStateService _state;
    private readonly SettingsService _settings;
    private readonly PdfExportService _pdf;

    public AdminHandler(ITelegramBotClient bot, AppDbContext db, BotConfig cfg,
                        AdminStateService state, SettingsService settings, PdfExportService pdf)
    {
        _bot = bot; _db = db; _cfg = cfg;
        _state = state; _settings = settings; _pdf = pdf;
    }

    private bool IsMainAdmin(long id) => id == _cfg.AdminId;

    // ══════════════════════════════════════════════════════════════════════
    //  TEXT
    // ══════════════════════════════════════════════════════════════════════
    public async Task<bool> HandleText(string text, long chatId, CancellationToken ct)
    {
        var s = _state.Get(chatId);

        if (text == "❌ Bekor qilish")
        {
            _state.Clear(chatId);
            await Send(chatId, "❌ Bekor qilindi.", AdminKeyboards.MainMenu(), ct);
            return true;
        }

        if (s.Step != AdminStep.None) return await HandleStep(text, chatId, s, ct);

        switch (text)
        {
            case "📊 Statistika": await ShowStats(chatId, ct); return true;
            case "👥 Foydalanuvchilar": await ShowUsersMenu(chatId, s, ct); return true;
            case "💳 To'lovlar": await ShowPendingPayments(chatId, ct); return true;
            case "⭐️ Obunalar": await ShowActiveSubs(chatId, ct); return true;
            case "📺 VIP Kanallar": await ShowChannels(chatId, ct); return true;
            case "📋 Rejalar": await ShowPlans(chatId, ct); return true;
            case "📢 Xabar yuborish": await StartBroadcast(chatId, s, ct); return true;
            case "⚙️ Sozlamalar": await ShowSettings(chatId, ct); return true;
            case "🔒 Majburiy obuna": await ShowMandatory(chatId, ct); return true;
            case "📡 Trader kanallar": await ShowTraderChannels(chatId, ct); return true;
            case "👑 Adminlar": await ShowAdmins(chatId, ct); return true;
            case "🌐 Web panel": await ShowWebPanel(chatId, ct); return true;
            case "🗑 Ma'lumotlarni tozalash": await ShowClearMenu(chatId, ct); return true;
            case "➕ Yangi kanal": await StartAddChannel(chatId, s, ct); return true;
            case "➕ Yangi reja": await StartAddPlan(chatId, s, ct); return true;
            case "🔍 Foydalanuvchi qidirish": await StartSearch(chatId, s, ct); return true;
            case "➕ Yangi trader kanal": await StartAddTrCh(chatId, s, ct); return true;
            case "➕ Admin qo'shish": await StartAddAdmin(chatId, s, ct); return true;
        }
        return false;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  STEP MACHINE
    // ══════════════════════════════════════════════════════════════════════
    private async Task<bool> HandleStep(string text, long chatId, AdminSession s, CancellationToken ct)
    {
        switch (s.Step)
        {
            case AdminStep.WaitChannelName:
                s.ChannelName = text.Trim(); s.Step = AdminStep.WaitChannelId;
                await Send(chatId, $"✅ Nom: <b>{s.ChannelName}</b>\n\nKanal Telegram ID:\n<code>-1001234567890</code>", AdminKeyboards.CancelMenu(), ct);
                return true;

            case AdminStep.WaitChannelId:
                if (!long.TryParse(text.Trim(), out var tgId))
                { await Send(chatId, "❗️ Noto'g'ri. Masalan: <code>-1001234567890</code>", AdminKeyboards.CancelMenu(), ct); return true; }
                await SaveChannel(chatId, s, tgId, ct); return true;

            case AdminStep.WaitPlanName:
                s.PlanName = text.Trim(); s.Step = AdminStep.WaitPlanDays;
                await Send(chatId, $"✅ Nom: <b>{s.PlanName}</b>\n\nNecha <b>kun</b>? (masalan: 30)", AdminKeyboards.CancelMenu(), ct);
                return true;

            case AdminStep.WaitPlanDays:
                if (!int.TryParse(text.Trim(), out var days) || days < 1)
                { await Send(chatId, "❗️ Musbat son kiriting.", AdminKeyboards.CancelMenu(), ct); return true; }
                s.PlanDays = days; s.Step = AdminStep.WaitPlanPrice;
                await Send(chatId, $"✅ {days} kun\n\n💰 <b>Narx</b> (UZS):", AdminKeyboards.CancelMenu(), ct);
                return true;

            case AdminStep.WaitPlanPrice:
                if (!decimal.TryParse(text.Trim(), out var price) || price < 0)
                { await Send(chatId, "❗️ To'g'ri narx kiriting.", AdminKeyboards.CancelMenu(), ct); return true; }
                s.PlanPrice = price; s.Step = AdminStep.WaitPlanChannel;
                var chs = await _db.VipChannels.Where(c => c.IsActive).ToListAsync(ct);
                if (chs.Count == 0) { await Send(chatId, "❗️ Avval VIP kanal qo'shing!", AdminKeyboards.MainMenu(), ct); _state.Clear(chatId); return true; }
                await _bot.SendMessage(chatId, $"✅ {price:N0} UZS\n\n📺 Qaysi kanalga?",
                    parseMode: ParseMode.Html, replyMarkup: AdminKeyboards.ChannelSelect(chs), cancellationToken: ct);
                return true;

            case AdminStep.WaitBalanceUserId:
                if (!int.TryParse(text.Trim(), out var uid))
                { await Send(chatId, "❗️ ID kiriting.", AdminKeyboards.CancelMenu(), ct); return true; }
                var tu = await _db.Users.FindAsync(new object[] { uid }, ct);
                if (tu is null) { await Send(chatId, $"❌ ID={uid} topilmadi.", AdminKeyboards.CancelMenu(), ct); return true; }
                s.TargetUserId = uid; s.Step = AdminStep.WaitBalanceAmount;
                await Send(chatId, $"👤 <b>{tu.FullName}</b>\n💰 {tu.Balance:N0} UZS\n\nMiqdor:", AdminKeyboards.CancelMenu(), ct);
                return true;

            case AdminStep.WaitBalanceAmount:
                if (!decimal.TryParse(text.Trim(), out var amt))
                { await Send(chatId, "❗️ Son kiriting.", AdminKeyboards.CancelMenu(), ct); return true; }
                await ProcessBalance(chatId, s.TargetUserId, amt, ct); _state.Clear(chatId); return true;

            case AdminStep.WaitBroadcastText:
                await ConfirmBroadcast(chatId, text, ct); return true;

            case AdminStep.WaitUserSearch:
                await SearchUser(chatId, text, ct); _state.Clear(chatId); return true;

            case AdminStep.WaitUserMessageText:
                await SendUserMsg(chatId, s.TargetUserId, text, ct); _state.Clear(chatId); return true;

            case AdminStep.WaitRejectReason:
                await ProcessReject(chatId, s.TargetPaymentId, text, ct); _state.Clear(chatId); return true;

            case AdminStep.WaitSettingValue:
                await _settings.Set(s.EditSettingKey, text.Trim(), ct);
                _state.Clear(chatId);
                await Send(chatId, $"✅ Saqlandi:\n<code>{text.Trim()}</code>", AdminKeyboards.MainMenu(), ct);
                return true;

            case AdminStep.WaitMandatoryChannelId:
                if (!long.TryParse(text.Trim(), out var mcid))
                { await Send(chatId, "❗️ Noto'g'ri format.", AdminKeyboards.CancelMenu(), ct); return true; }
                var cur = await _settings.Get(SettingsService.MandatoryChannels, "", ct);
                await _settings.Set(SettingsService.MandatoryChannels,
                    string.IsNullOrEmpty(cur) ? $"{mcid}" : $"{cur},{mcid}", ct);
                _state.Clear(chatId);
                await Send(chatId, $"✅ Kanal qo'shildi: <code>{mcid}</code>", AdminKeyboards.MainMenu(), ct);
                return true;

            case AdminStep.WaitNewAdminId:
                if (!long.TryParse(text.Trim(), out var newAdminId))
                { await Send(chatId, "❗️ Telegram ID kiriting.", AdminKeyboards.CancelMenu(), ct); return true; }
                await AddAdmin(chatId, newAdminId, ct); _state.Clear(chatId); return true;

            // Trader kanal
            case AdminStep.WaitTrChTitle:
                s.TrChTitle = text.Trim(); s.Step = AdminStep.WaitTrChEmoji;
                await Send(chatId, $"✅ Nom: {s.TrChTitle}\n\nEmoji (masalan: 📢, 📡, 🎯):", AdminKeyboards.CancelMenu(), ct);
                return true;

            case AdminStep.WaitTrChEmoji:
                s.TrChEmoji = text.Trim(); s.Step = AdminStep.WaitTrChDesc;
                await Send(chatId, "Qisqa tavsif (masalan: Bepul signallar):", AdminKeyboards.CancelMenu(), ct);
                return true;

            case AdminStep.WaitTrChDesc:
                s.TrChDesc = text.Trim(); s.Step = AdminStep.WaitTrChLink;
                await Send(chatId, "Kanal linki (https://t.me/...):", AdminKeyboards.CancelMenu(), ct);
                return true;

            case AdminStep.WaitTrChLink:
                s.TrChLink = text.Trim();
                if (!s.TrChLink.StartsWith("https://") && !s.TrChLink.StartsWith("http://"))
                    s.TrChLink = "https://t.me/" + s.TrChLink.TrimStart('@');
                await SaveTrCh(chatId, s, ct); return true;
        }
        return false;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CALLBACK
    // ══════════════════════════════════════════════════════════════════════
    public async Task<bool> HandleCallback(CallbackQuery cb, CancellationToken ct)
    {
        var data = cb.Data ?? "";
        var chatId = cb.Message!.Chat.Id;
        var msgId = cb.Message.MessageId;
        if (!data.StartsWith("adm_")) return false;

        await _bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
        var s = _state.Get(chatId);

        if (data == "adm_cancel")
        { _state.Clear(chatId); try { await _bot.EditMessageText(chatId, msgId, "❌ Bekor qilindi.", cancellationToken: ct); } catch { } return true; }

        if (data.StartsWith("adm_approve:"))
        { await ApprovePayment(int.Parse(data.Substring(12)), chatId, msgId, ct); return true; }

        if (data.StartsWith("adm_reject:"))
        {
            s.TargetPaymentId = int.Parse(data.Substring(11)); s.Step = AdminStep.WaitRejectReason;
            await Send(chatId, "❌ Rad etish sababini kiriting (<code>-</code> bo'sh uchun):", AdminKeyboards.CancelMenu(), ct);
            return true;
        }

        if (data.StartsWith("adm_viewuser_pay:"))
        {
            var p = await _db.Payments.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == int.Parse(data.Substring(17)), ct);
            if (p is not null) await ShowUserCard(chatId, p.User, ct);
            return true;
        }

        if (data.StartsWith("adm_addbal:"))
        {
            s.TargetUserId = int.Parse(data.Substring(11)); s.Step = AdminStep.WaitBalanceAmount;
            var u = await _db.Users.FindAsync(new object[] { s.TargetUserId }, ct);
            await Send(chatId, $"👤 <b>{u?.FullName}</b>\n💰 {u?.Balance:N0} UZS\n\nMiqdor:", AdminKeyboards.CancelMenu(), ct);
            return true;
        }

        if (data.StartsWith("adm_msg:"))
        {
            s.TargetUserId = int.Parse(data.Substring(8)); s.Step = AdminStep.WaitUserMessageText;
            await Send(chatId, "✉️ Xabar matnini kiriting:", AdminKeyboards.CancelMenu(), ct);
            return true;
        }

        if (data.StartsWith("adm_ban:")) { await BanUser(chatId, int.Parse(data.Substring(8)), true, ct); return true; }
        if (data.StartsWith("adm_unban:")) { await BanUser(chatId, int.Parse(data.Substring(10)), false, ct); return true; }

        if (data.StartsWith("adm_makeadmin:"))
        {
            var uid = int.Parse(data.Substring(14));
            var u = await _db.Users.FindAsync(new object[] { uid }, ct);
            if (u is not null) await AddAdmin(chatId, u.TelegramId, ct);
            return true;
        }

        if (data.StartsWith("adm_givevip:"))
        {
            var uid = int.Parse(data.Substring(12));
            var plans = await _db.Plans.Where(p => p.IsActive).ToListAsync(ct);
            await _bot.EditMessageText(chatId, msgId, "⭐️ Qaysi reja?",
                replyMarkup: AdminKeyboards.GiveVipPlans(plans, uid), cancellationToken: ct);
            return true;
        }

        if (data.StartsWith("adm_givevip_plan:"))
        {
            var parts = data.Substring(17).Split(':');
            await GiveVip(chatId, msgId, int.Parse(parts[0]), int.Parse(parts[1]), ct);
            return true;
        }

        // Kanal
        if (data.StartsWith("adm_editchan:"))
        {
            var cid = int.Parse(data.Substring(13)); var ch = await _db.VipChannels.FindAsync(new object[] { cid }, ct);
            if (ch is null) return true;
            s.EditChannelId = cid; s.ChannelName = ch.Title; s.Step = AdminStep.WaitChannelName;
            await Send(chatId, $"✏️ Yangi nom (hozir: {ch.Title}):", AdminKeyboards.CancelMenu(), ct);
            return true;
        }

        if (data.StartsWith("adm_activechan:") || data.StartsWith("adm_deactivechan:"))
        {
            var act = data.StartsWith("adm_activechan:");
            var cid = int.Parse(data.Substring(act ? 15 : 17));
            var ch = await _db.VipChannels.FindAsync(new object[] { cid }, ct);
            if (ch is not null) { ch.IsActive = act; await _db.SaveChangesAsync(ct); }
            await ShowChannels(chatId, ct); return true;
        }

        if (data.StartsWith("adm_delchan:"))
        {
            var cid = int.Parse(data.Substring(12));
            if (await _db.Plans.AnyAsync(p => p.VipChannelId == cid, ct))
            { await _bot.AnswerCallbackQuery(cb.Id, "Kanalda rejalar bor!", showAlert: true, cancellationToken: ct); return true; }
            var ch = await _db.VipChannels.FindAsync(new object[] { cid }, ct);
            if (ch is not null) { _db.VipChannels.Remove(ch); await _db.SaveChangesAsync(ct); }
            await ShowChannels(chatId, ct); return true;
        }

        // Reja
        if (data.StartsWith("adm_editplan:"))
        {
            var pid = int.Parse(data.Substring(13)); var p = await _db.Plans.FindAsync(new object[] { pid }, ct);
            if (p is null) return true;
            s.EditPlanId = pid; s.PlanName = p.Title; s.PlanDays = p.DurationDays; s.PlanPrice = p.Price; s.PlanChannelId = p.VipChannelId;
            s.Step = AdminStep.WaitPlanName;
            await Send(chatId, $"✏️ Yangi nom (hozir: {p.Title}):", AdminKeyboards.CancelMenu(), ct);
            return true;
        }

        if (data.StartsWith("adm_activeplan:") || data.StartsWith("adm_deactiveplan:"))
        {
            var act = data.StartsWith("adm_activeplan:");
            var pid = int.Parse(data.Substring(act ? 15 : 17));
            var p = await _db.Plans.FindAsync(new object[] { pid }, ct);
            if (p is not null) { p.IsActive = act; await _db.SaveChangesAsync(ct); }
            await ShowPlans(chatId, ct); return true;
        }

        if (data.StartsWith("adm_planchan:"))
        { s.PlanChannelId = int.Parse(data.Substring(13)); await SavePlan(chatId, s, ct); return true; }

        // Sozlamalar
        if (data.StartsWith("adm_set:"))
        {
            var key = data.Substring(8); s.EditSettingKey = key; s.Step = AdminStep.WaitSettingValue;
            var val = await _settings.Get(key, "", ct);
            var lbl = new Dictionary<string, string>
            {
                [SettingsService.CardNumber] = "💳 Karta raqami",
                [SettingsService.CardOwner] = "👤 Karta egasi",
                [SettingsService.SupportUsername] = "📞 Support",
                [SettingsService.WelcomeMessage] = "👋 Salomlashuv xabari",
                [SettingsService.BotAboutText] = "ℹ️ Bot haqida",
                [SettingsService.NotifyHoursBefore] = "🔔 Eslatma (soat)",
                [SettingsService.WebAdminUrl] = "🌐 Web panel URL",
            };
            var label = lbl.TryGetValue(key, out var l) ? l : key;
            await Send(chatId, $"{label}\n\nHozirgi:\n<code>{val}</code>\n\nYangi qiymat:", AdminKeyboards.CancelMenu(), ct);
            return true;
        }

        // Majburiy obuna
        if (data == "adm_mandatory_on") { await _settings.Set(SettingsService.MandatoryEnabled, "true", ct); await ShowMandatory(chatId, ct); return true; }
        if (data == "adm_mandatory_off") { await _settings.Set(SettingsService.MandatoryEnabled, "false", ct); await ShowMandatory(chatId, ct); return true; }
        if (data == "adm_mandatory_add") { s.Step = AdminStep.WaitMandatoryChannelId; await Send(chatId, "Kanal ID kiriting (<code>-1001234567890</code>):", AdminKeyboards.CancelMenu(), ct); return true; }
        if (data == "adm_mandatory_clear") { await _settings.Set(SettingsService.MandatoryChannels, "", ct); await ShowMandatory(chatId, ct); return true; }

        // Trader kanal
        if (data.StartsWith("adm_edittrch:"))
        {
            var id = int.Parse(data.Substring(13)); var ch = await _db.TraderChannels.FindAsync(new object[] { id }, ct);
            if (ch is null) return true;
            s.EditTrChId = id; s.TrChTitle = ch.Title; s.TrChEmoji = ch.Emoji;
            s.TrChDesc = ch.Description; s.TrChLink = ch.Link; s.Step = AdminStep.WaitTrChTitle;
            await Send(chatId, $"✏️ Yangi nom (hozir: {ch.Title}):", AdminKeyboards.CancelMenu(), ct);
            return true;
        }

        if (data.StartsWith("adm_activetrch:") || data.StartsWith("adm_deactivetrch:"))
        {
            var act = data.StartsWith("adm_activetrch:");
            var id = int.Parse(data.Substring(act ? 15 : 17));
            var ch = await _db.TraderChannels.FindAsync(new object[] { id }, ct);
            if (ch is not null) { ch.IsActive = act; await _db.SaveChangesAsync(ct); }
            await ShowTraderChannels(chatId, ct); return true;
        }

        if (data.StartsWith("adm_deltrch:"))
        {
            var id = int.Parse(data.Substring(12));
            var ch = await _db.TraderChannels.FindAsync(new object[] { id }, ct);
            if (ch is not null) { _db.TraderChannels.Remove(ch); await _db.SaveChangesAsync(ct); }
            await ShowTraderChannels(chatId, ct); return true;
        }

        // Admin o'chirish
        if (data.StartsWith("adm_deladmin:"))
        {
            var id = int.Parse(data.Substring(13));
            var a = await _db.AdminUsers.FindAsync(new object[] { id }, ct);
            if (a is not null) { _db.AdminUsers.Remove(a); await _db.SaveChangesAsync(ct); }
            await ShowAdmins(chatId, ct); return true;
        }

        // Ma'lumotlarni tozalash
        if (data == "adm_clear_users")
        {
            await _bot.EditMessageText(chatId, msgId,
                "⚠️ Barcha foydalanuvchilarni o'chirasizmi? Bu qaytarib bo'lmaydi!",
                replyMarkup: AdminKeyboards.Confirm("adm_clear_users_yes", "🗑 Ha, o'chir"), cancellationToken: ct);
            return true;
        }
        if (data == "adm_clear_users_yes")
        {
            await _db.Payments.ExecuteDeleteAsync(ct);
            await _db.Subscriptions.ExecuteDeleteAsync(ct);
            await _db.Users.ExecuteDeleteAsync(ct);
            await _bot.EditMessageText(chatId, msgId, "✅ Barcha foydalanuvchilar o'chirildi.", cancellationToken: ct);
            return true;
        }
        if (data == "adm_clear_payments")
        {
            await _db.Payments.ExecuteDeleteAsync(ct);
            await _bot.EditMessageText(chatId, msgId, "✅ Barcha to'lovlar o'chirildi.", cancellationToken: ct);
            return true;
        }
        if (data == "adm_clear_subs")
        {
            await _db.Subscriptions.ExecuteDeleteAsync(ct);
            await _bot.EditMessageText(chatId, msgId, "✅ Barcha obunalar o'chirildi.", cancellationToken: ct);
            return true;
        }
        if (data == "adm_clear_all")
        {
            await _bot.EditMessageText(chatId, msgId,
                "⚠️ HAMMA MA'LUMOTLARNI o'chirasizmi? Bu qaytarib bo'lmaydi!",
                replyMarkup: AdminKeyboards.Confirm("adm_clear_all_yes", "⚠️ Ha, hammasini o'chir"), cancellationToken: ct);
            return true;
        }
        if (data == "adm_clear_all_yes")
        {
            await _db.Payments.ExecuteDeleteAsync(ct);
            await _db.Subscriptions.ExecuteDeleteAsync(ct);
            await _db.Users.ExecuteDeleteAsync(ct);
            await _bot.EditMessageText(chatId, msgId, "✅ Hammasi o'chirildi. Bot tozalandi.", cancellationToken: ct);
            return true;
        }

        // Broadcast
        if (data.StartsWith("adm_broadcast_yes:"))
        {
            var text = System.Net.WebUtility.UrlDecode(data.Substring(18));
            await ExecuteBroadcast(chatId, msgId, text, ct); return true;
        }

        // PDF export
        if (data == "adm_pdf_users")
        {
            await ExportUsersPdf(chatId, ct); return true;
        }
        if (data == "adm_pdf_payments")
        {
            await ExportPaymentsPdf(chatId, ct); return true;
        }

        return false;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  STATISTIKA
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowStats(long chatId, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var month = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var total = await _db.Users.CountAsync(ct);
        var todayNew = await _db.Users.CountAsync(u => u.CreatedAt >= today, ct);
        var activeVip = await _db.Subscriptions.CountAsync(s => s.IsActive && s.ExpireDate > DateTime.UtcNow, ct);
        var pending = await _db.Payments.CountAsync(p => p.Status == PaymentStatus.Pending, ct);
        var todayInc = await _db.Payments.Where(p => p.Status == PaymentStatus.Approved && p.CreatedAt >= today).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var monInc = await _db.Payments.Where(p => p.Status == PaymentStatus.Approved && p.CreatedAt >= month).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var totInc = await _db.Payments.Where(p => p.Status == PaymentStatus.Approved).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;

        await _bot.SendMessage(chatId,
            $"📊 <b>Statistika</b> — {TimeHelper.NowTashkent():dd.MM.yyyy HH:mm}\n" +
            $"━━━━━━━━━━━━━━━━━━━━\n" +
            $"👥 Jami:              <b>{total:N0}</b>\n" +
            $"📅 Bugun:             <b>{todayNew}</b>\n" +
            $"⭐️ Faol VIP:         <b>{activeVip}</b>\n" +
            $"💳 Kutayotgan:        <b>{pending}</b>\n" +
            $"━━━━━━━━━━━━━━━━━━━━\n" +
            $"💰 Bugun:             <b>{todayInc:N0} UZS</b>\n" +
            $"💰 Oy:                <b>{monInc:N0} UZS</b>\n" +
            $"💰 Jami:              <b>{totInc:N0} UZS</b>",
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("📄 PDF - Foydalanuvchilar", "adm_pdf_users")   },
                new[] { InlineKeyboardButton.WithCallbackData("📄 PDF - To'lovlar",        "adm_pdf_payments") }
            }), cancellationToken: ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PDF EXPORT
    // ══════════════════════════════════════════════════════════════════════
    private async Task ExportUsersPdf(long chatId, CancellationToken ct)
    {
        await _bot.SendMessage(chatId, "⏳ PDF tayyorlanmoqda...", cancellationToken: ct);
        try
        {
            var bytes = await _pdf.GenerateUsersPdfAsync(ct);
            var fileName = $"users_{TimeHelper.NowTashkent():yyyyMMdd_HHmmss}.html";
            using var stream = new MemoryStream(bytes);
            await _bot.SendDocument(chatId,
                InputFile.FromStream(stream, fileName),
                caption: $"📄 Foydalanuvchilar ro'yxati\n{TimeHelper.NowTashkent():dd.MM.yyyy HH:mm}",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await Send(chatId, $"❌ Xato: {ex.Message}", AdminKeyboards.MainMenu(), ct);
        }
    }

    private async Task ExportPaymentsPdf(long chatId, CancellationToken ct)
    {
        await _bot.SendMessage(chatId, "⏳ PDF tayyorlanmoqda...", cancellationToken: ct);
        try
        {
            var bytes = await _pdf.GeneratePaymentsPdfAsync(ct);
            var fileName = $"payments_{TimeHelper.NowTashkent():yyyyMMdd_HHmmss}.html";
            using var stream = new MemoryStream(bytes);
            await _bot.SendDocument(chatId,
                InputFile.FromStream(stream, fileName),
                caption: $"📄 To'lovlar tarixi\n{TimeHelper.NowTashkent():dd.MM.yyyy HH:mm}",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await Send(chatId, $"❌ Xato: {ex.Message}", AdminKeyboards.MainMenu(), ct);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  FOYDALANUVCHILAR
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowUsersMenu(long chatId, AdminSession s, CancellationToken ct)
    {
        var total = await _db.Users.CountAsync(ct);
        var active = await _db.Subscriptions.CountAsync(sub => sub.IsActive && sub.ExpireDate > DateTime.UtcNow, ct);
        var today = DateTime.UtcNow.Date;
        var todayNew = await _db.Users.CountAsync(u => u.CreatedAt >= today, ct);
        var last5 = await _db.Users.OrderByDescending(u => u.CreatedAt).Take(5)
            .Select(u => new { u.Id, u.FullName, u.CreatedAt }).ToListAsync(ct);
        var lastList = string.Join("\n", last5.Select(u => $"  • <code>{u.Id}</code> {u.FullName} ({u.CreatedAt.TashkentShort()})"));

        await _bot.SendMessage(chatId,
            $"👥 <b>Foydalanuvchilar</b>\n\n" +
            $"Jami: <b>{total}</b> | Bugun: <b>{todayNew}</b> | VIP: <b>{active}</b>\n\n" +
            $"<b>Oxirgi 5 ta:</b>\n{lastList}",
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🔍 Qidirish",         "adm_search_user") },
                new[] { InlineKeyboardButton.WithCallbackData("📄 PDF ro'yxat",      "adm_pdf_users")   }
            }), cancellationToken: ct);
    }

    private async Task StartSearch(long chatId, AdminSession s, CancellationToken ct)
    {
        s.Step = AdminStep.WaitUserSearch;
        await Send(chatId, "🔍 Ism, telefon yoki ID kiriting:", AdminKeyboards.CancelMenu(), ct);
    }

    private async Task SearchUser(long chatId, string q, CancellationToken ct)
    {
        IQueryable<TBUser> query = _db.Users;
        if (int.TryParse(q, out var id)) query = query.Where(u => u.Id == id || u.TelegramId == id);
        else query = query.Where(u => u.FullName.Contains(q) || u.PhoneNumber.Contains(q));

        var users = await query.Take(5).ToListAsync(ct);
        if (users.Count == 0) { await Send(chatId, "❌ Topilmadi.", AdminKeyboards.MainMenu(), ct); return; }
        foreach (var u in users) await ShowUserCard(chatId, u, ct);
    }

    private async Task ShowUserCard(long chatId, TBUser u, CancellationToken ct)
    {
        var sub = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == u.Id && s.IsActive && s.ExpireDate > DateTime.UtcNow)
            .FirstOrDefaultAsync(ct);
        var subLine = sub is not null ? $"⭐️ VIP: <b>{sub.Plan.Title}</b> ({sub.ExpireDate.TashkentDate()} gacha)" : "⭐️ VIP: Yo'q";

        await _bot.SendMessage(chatId,
            $"👤 <b>#{u.Id} — {u.FullName}</b>\n" +
            $"📱 <code>{u.PhoneNumber}</code>\n" +
            $"💰 <b>{u.Balance:N0} UZS</b>\n" +
            $"🆔 TG: <code>{u.TelegramId}</code>\n" +
            $"{subLine}\n" +
            $"{(u.IsBanned ? "🚫 Bloklangan" : "✅ Faol")} | {u.CreatedAt.TashkentDate()}",
            parseMode: ParseMode.Html,
            replyMarkup: AdminKeyboards.UserActions(u.Id, u.IsBanned), cancellationToken: ct);
    }

    private async Task BanUser(long chatId, int userId, bool ban, CancellationToken ct)
    {
        var u = await _db.Users.FindAsync(new object[] { userId }, ct);
        if (u is null) return;
        u.IsBanned = ban; await _db.SaveChangesAsync(ct);
        try { await _bot.SendMessage(u.TelegramId, ban ? $"🚫 <b>Hisobingiz bloklandi.</b>" : "✅ <b>Hisobingiz faollashtirildi.</b>", parseMode: ParseMode.Html, cancellationToken: ct); } catch { }
        await Send(chatId, ban ? $"🚫 {u.FullName} bloklandi." : $"✅ {u.FullName} blokdan chiqarildi.", AdminKeyboards.MainMenu(), ct);
    }

    private async Task ProcessBalance(long chatId, int userId, decimal amount, CancellationToken ct)
    {
        var u = await _db.Users.FindAsync(new object[] { userId }, ct);
        if (u is null) { await Send(chatId, "❌ Topilmadi.", AdminKeyboards.MainMenu(), ct); return; }
        u.Balance += amount; await _db.SaveChangesAsync(ct);
        try { await _bot.SendMessage(u.TelegramId, $"💰 <b>Balans yangilandi</b>\n\n{(amount >= 0 ? "+" : "")}{amount:N0} UZS\nBalans: <b>{u.Balance:N0} UZS</b>", parseMode: ParseMode.Html, cancellationToken: ct); } catch { }
        await Send(chatId, $"✅ {u.FullName}: {(amount >= 0 ? "+" : "")}{amount:N0} UZS\nYangi balans: <b>{u.Balance:N0} UZS</b>", AdminKeyboards.MainMenu(), ct);
    }

    private async Task SendUserMsg(long chatId, int userId, string text, CancellationToken ct)
    {
        var u = await _db.Users.FindAsync(new object[] { userId }, ct);
        if (u is null) { await Send(chatId, "❌ Topilmadi.", AdminKeyboards.MainMenu(), ct); return; }
        try { await _bot.SendMessage(u.TelegramId, $"📢 <b>Admin xabari:</b>\n\n{text}", parseMode: ParseMode.Html, cancellationToken: ct); await Send(chatId, $"✅ {u.FullName} ga yuborildi.", AdminKeyboards.MainMenu(), ct); }
        catch (Exception ex) { await Send(chatId, $"❌ {ex.Message}", AdminKeyboards.MainMenu(), ct); }
    }

    private async Task GiveVip(long chatId, int msgId, int userId, int planId, CancellationToken ct)
    {
        var plan = await _db.Plans.Include(p => p.VipChannel).FirstOrDefaultAsync(p => p.Id == planId, ct);
        var user = await _db.Users.FindAsync(new object[] { userId }, ct);
        if (plan is null || user is null) return;
        var exp = await CreateOrExtendSub(userId, planId, plan.DurationDays, ct);
        string inv; try { var lnk = await _bot.CreateChatInviteLink(plan.VipChannel.TelegramChannelId, memberLimit: 1, expireDate: DateTime.UtcNow.AddDays(1), cancellationToken: ct); inv = $"\n\n🔗 {lnk.InviteLink}"; } catch { inv = ""; }
        await _bot.SendMessage(user.TelegramId, $"🎁 <b>Admin VIP berdi!</b>\n\n⭐️ {plan.Title} | 📅 {exp.TashkentDate()} gacha{inv}", parseMode: ParseMode.Html, cancellationToken: ct);
        try { await _bot.EditMessageText(chatId, msgId, $"✅ {user.FullName} ga {plan.Title} berildi ({exp.TashkentDate()} gacha).", cancellationToken: ct); } catch { }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TO'LOVLAR
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowPendingPayments(long chatId, CancellationToken ct)
    {
        var pending = await _db.Payments.Include(p => p.User).Include(p => p.Plan)
            .Where(p => p.Status == PaymentStatus.Pending).OrderBy(p => p.CreatedAt).Take(10).ToListAsync(ct);

        if (pending.Count == 0) { await Send(chatId, "✅ Kutayotgan to'lovlar yo'q.", AdminKeyboards.MainMenu(), ct); return; }
        await Send(chatId, $"💳 <b>Kutayotgan to'lovlar: {pending.Count} ta</b>", AdminKeyboards.MainMenu(), ct);

        foreach (var p in pending)
        {
            var txt =
                $"💳 <b>To'lov #{p.Id}</b>\n" +
                $"👤 {p.User.FullName} | 📱 {p.User.PhoneNumber}\n" +
                $"🆔 UserID: <code>{p.UserId}</code>\n" +
                $"⭐️ {p.Plan?.Title ?? "Hisob to'ldirish"}\n" +
                $"💰 <b>{p.Amount:N0} UZS</b>\n" +
                $"🕐 {p.CreatedAt.TashkentFull()}";

            if (!string.IsNullOrEmpty(p.ReceiptFileId))
                await _bot.SendPhoto(chatId, InputFile.FromFileId(p.ReceiptFileId), caption: txt, parseMode: ParseMode.Html, replyMarkup: AdminKeyboards.PaymentActions(p.Id), cancellationToken: ct);
            else
                await _bot.SendMessage(chatId, txt, parseMode: ParseMode.Html, replyMarkup: AdminKeyboards.PaymentActions(p.Id), cancellationToken: ct);
        }
    }

    private async Task ApprovePayment(int paymentId, long chatId, int msgId, CancellationToken ct)
    {
        var p = await _db.Payments.Include(p => p.User).Include(p => p.Plan).ThenInclude(p => p!.VipChannel)
            .FirstOrDefaultAsync(x => x.Id == paymentId, ct);
        if (p is null || p.Status != PaymentStatus.Pending)
        { try { await _bot.EditMessageText(chatId, msgId, "⚠️ Allaqachon ko'rib chiqilgan.", cancellationToken: ct); } catch { } return; }

        p.Status = PaymentStatus.Approved;
        if (p.Type == PaymentType.TopUp)
        {
            p.User.Balance += p.Amount; await _db.SaveChangesAsync(ct);
            await _bot.SendMessage(p.User.TelegramId, $"✅ <b>Hisob to'ldirildi!</b>\n\n+{p.Amount:N0} UZS\nBalans: <b>{p.User.Balance:N0} UZS</b>", parseMode: ParseMode.Html, cancellationToken: ct);
        }
        else if (p.Plan is not null)
        {
            await _db.SaveChangesAsync(ct);
            var exp = await CreateOrExtendSub(p.UserId, p.PlanId!.Value, p.Plan.DurationDays, ct);
            string inv; try { var lnk = await _bot.CreateChatInviteLink(p.Plan.VipChannel.TelegramChannelId, memberLimit: 1, expireDate: DateTime.UtcNow.AddDays(1), cancellationToken: ct); inv = $"\n\n🔗 <b>VIP havola:</b>\n{lnk.InviteLink}\n\n⚠️ 1 marta ishlaydi!"; } catch { inv = ""; }
            await _bot.SendMessage(p.User.TelegramId, $"✅ <b>VIP Tasdiqlandi!</b>\n\n⭐️ {p.Plan.Title}\n📅 {exp.TashkentDate()} gacha{inv}", parseMode: ParseMode.Html, cancellationToken: ct);
        }

        try { await _bot.EditMessageCaption(chatId, msgId, caption: $"✅ <b>TASDIQLANDI</b> — {TimeHelper.NowTashkent():HH:mm:ss}", parseMode: ParseMode.Html, cancellationToken: ct); }
        catch { try { await _bot.EditMessageText(chatId, msgId, $"✅ To'lov #{paymentId} tasdiqlandi.", cancellationToken: ct); } catch { } }
    }

    private async Task ProcessReject(long chatId, int paymentId, string reason, CancellationToken ct)
    {
        var p = await _db.Payments.Include(p => p.User).FirstOrDefaultAsync(x => x.Id == paymentId, ct);
        if (p is null || p.Status != PaymentStatus.Pending) { await Send(chatId, "⚠️ Allaqachon ko'rib chiqilgan.", AdminKeyboards.MainMenu(), ct); return; }
        p.Status = PaymentStatus.Rejected; await _db.SaveChangesAsync(ct);
        var sup = await _settings.Get(SettingsService.SupportUsername, "@support", ct);
        try { await _bot.SendMessage(p.User.TelegramId, $"❌ <b>To'lovingiz rad etildi.</b>\n\nSabab: {(reason.Trim() == "-" ? "Chek tasdiqlanmadi" : reason)}\n\n📞 {sup}", parseMode: ParseMode.Html, cancellationToken: ct); } catch { }
        await Send(chatId, $"❌ To'lov #{paymentId} rad etildi.", AdminKeyboards.MainMenu(), ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  OBUNALAR
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowActiveSubs(long chatId, CancellationToken ct)
    {
        var subs = await _db.Subscriptions.Include(s => s.User).Include(s => s.Plan)
            .Where(s => s.IsActive && s.ExpireDate > DateTime.UtcNow)
            .OrderBy(s => s.ExpireDate).Take(15).ToListAsync(ct);
        if (subs.Count == 0) { await Send(chatId, "⭐️ Faol obunalar yo'q.", AdminKeyboards.MainMenu(), ct); return; }
        var lines = subs.Select(s => $"• {s.User.FullName} — {s.Plan.Title} — <b>{(int)(s.ExpireDate - DateTime.UtcNow).TotalDays} kun</b>");
        await Send(chatId, $"⭐️ <b>Faol obunalar ({subs.Count} ta)</b>\n\n" + string.Join("\n", lines), AdminKeyboards.MainMenu(), ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  VIP KANALLAR
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowChannels(long chatId, CancellationToken ct)
    {
        var channels = await _db.VipChannels.Include(c => c.Plans).ToListAsync(ct);
        await _bot.SendMessage(chatId,
            channels.Count == 0 ? "📺 <b>VIP Kanallar</b>\n\nHozircha kanal yo'q." : $"📺 <b>VIP Kanallar ({channels.Count} ta)</b>",
            parseMode: ParseMode.Html,
            replyMarkup: new ReplyKeyboardMarkup(new[] { new[] { new KeyboardButton("➕ Yangi kanal"), new KeyboardButton("❌ Bekor qilish") } }) { ResizeKeyboard = true },
            cancellationToken: ct);
        foreach (var c in channels)
            await _bot.SendMessage(chatId,
                $"{(c.IsActive ? "🟢" : "🔴")} <b>{c.Title}</b>\n<code>{c.TelegramChannelId}</code> | {c.Plans.Count(p => p.IsActive)} reja",
                parseMode: ParseMode.Html, replyMarkup: AdminKeyboards.ChannelActions(c.Id, c.IsActive), cancellationToken: ct);
    }

    private async Task StartAddChannel(long chatId, AdminSession s, CancellationToken ct)
    {
        s.EditChannelId = null; s.Step = AdminStep.WaitChannelName;
        await Send(chatId, "📺 <b>Yangi VIP Kanal</b>\n\nKanal nomini kiriting:", AdminKeyboards.CancelMenu(), ct);
    }

    private async Task SaveChannel(long chatId, AdminSession s, long tgId, CancellationToken ct)
    {
        if (s.EditChannelId.HasValue)
        {
            var ch = await _db.VipChannels.FindAsync(new object[] { s.EditChannelId.Value }, ct);
            if (ch is not null) { ch.Title = s.ChannelName; ch.TelegramChannelId = tgId; }
        }
        else _db.VipChannels.Add(new VipChannel { Title = s.ChannelName, TelegramChannelId = tgId, IsActive = true });
        await _db.SaveChangesAsync(ct); _state.Clear(chatId);
        await Send(chatId, $"✅ Kanal {(s.EditChannelId.HasValue ? "yangilandi" : "qo'shildi")}!\n\n<b>{s.ChannelName}</b>\n<code>{tgId}</code>", AdminKeyboards.MainMenu(), ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  REJALAR
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowPlans(long chatId, CancellationToken ct)
    {
        var plans = await _db.Plans.Include(p => p.VipChannel).OrderBy(p => p.VipChannelId).ThenBy(p => p.Price).ToListAsync(ct);
        await _bot.SendMessage(chatId,
            plans.Count == 0 ? "📋 Rejalar yo'q." : $"📋 <b>Rejalar ({plans.Count} ta)</b>",
            parseMode: ParseMode.Html,
            replyMarkup: new ReplyKeyboardMarkup(new[] { new[] { new KeyboardButton("➕ Yangi reja"), new KeyboardButton("❌ Bekor qilish") } }) { ResizeKeyboard = true },
            cancellationToken: ct);
        foreach (var p in plans)
            await _bot.SendMessage(chatId,
                $"{(p.IsActive ? "🟢" : "🔴")} <b>{p.Title}</b>\n📺 {p.VipChannel.Title} | 📅 {p.DurationDays} kun | 💰 {p.Price:N0} UZS",
                parseMode: ParseMode.Html, replyMarkup: AdminKeyboards.PlanActions(p.Id, p.IsActive), cancellationToken: ct);
    }

    private async Task StartAddPlan(long chatId, AdminSession s, CancellationToken ct)
    {
        s.EditPlanId = null; s.Step = AdminStep.WaitPlanName;
        await Send(chatId, "📋 <b>Yangi Reja</b>\n\nNomini kiriting:", AdminKeyboards.CancelMenu(), ct);
    }

    private async Task SavePlan(long chatId, AdminSession s, CancellationToken ct)
    {
        if (s.EditPlanId.HasValue)
        {
            var p = await _db.Plans.FindAsync(new object[] { s.EditPlanId.Value }, ct);
            if (p is not null) { p.Title = s.PlanName; p.DurationDays = s.PlanDays; p.Price = s.PlanPrice; p.VipChannelId = s.PlanChannelId; }
        }
        else _db.Plans.Add(new Plan { Title = s.PlanName, DurationDays = s.PlanDays, Price = s.PlanPrice, VipChannelId = s.PlanChannelId, IsActive = true });
        await _db.SaveChangesAsync(ct); _state.Clear(chatId);
        await Send(chatId, $"✅ Reja {(s.EditPlanId.HasValue ? "yangilandi" : "qo'shildi")}!\n\n<b>{s.PlanName}</b> | {s.PlanDays} kun | {s.PlanPrice:N0} UZS", AdminKeyboards.MainMenu(), ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BROADCAST
    // ══════════════════════════════════════════════════════════════════════
    private async Task StartBroadcast(long chatId, AdminSession s, CancellationToken ct)
    {
        s.Step = AdminStep.WaitBroadcastText;
        await Send(chatId, "📢 Xabar matnini kiriting:\n\nHTML: <code>&lt;b&gt;qalin&lt;/b&gt;</code>, <code>&lt;i&gt;kursiv&lt;/i&gt;</code>", AdminKeyboards.CancelMenu(), ct);
    }

    private async Task ConfirmBroadcast(long chatId, string text, CancellationToken ct)
    {
        _state.Clear(chatId);
        var total = await _db.Users.CountAsync(u => !u.IsBanned && u.UserStep == "Active", ct);
        var encoded = System.Net.WebUtility.UrlEncode(text);
        await _bot.SendMessage(chatId,
            $"📢 <b>Ko'rinishi:</b>\n\n{text}\n\n━━━━━━━━━━\n{total} ta foydalanuvchiga yuboriladi.",
            parseMode: ParseMode.Html,
            replyMarkup: AdminKeyboards.Confirm($"adm_broadcast_yes:{encoded}", "📢 Yuborish"), cancellationToken: ct);
    }

    private async Task ExecuteBroadcast(long chatId, int msgId, string text, CancellationToken ct)
    {
        var ids = await _db.Users.Where(u => !u.IsBanned && u.UserStep == "Active").Select(u => u.TelegramId).ToListAsync(ct);
        await _bot.EditMessageText(chatId, msgId, $"📢 Yuborilmoqda 0/{ids.Count}...", cancellationToken: ct);
        int sent = 0, failed = 0;
        foreach (var id in ids)
        {
            try { await _bot.SendMessage(id, $"📢 <b>Xabar</b>\n\n{text}", parseMode: ParseMode.Html, cancellationToken: ct); sent++; }
            catch { failed++; }
            if (sent % 20 == 0) await Task.Delay(1000, ct);
        }
        await _bot.EditMessageText(chatId, msgId, $"✅ <b>Broadcast tugadi</b>\n\n✅ {sent} | ❌ {failed} | Jami {ids.Count}", parseMode: ParseMode.Html, cancellationToken: ct);
        await Send(chatId, "✅ Broadcast tugadi.", AdminKeyboards.MainMenu(), ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SOZLAMALAR
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowSettings(long chatId, CancellationToken ct)
    {
        var card = await _settings.Get(SettingsService.CardNumber, _cfg.CardNumber, ct);
        var owner = await _settings.Get(SettingsService.CardOwner, _cfg.CardOwner, ct);
        var sup = await _settings.Get(SettingsService.SupportUsername, "@support", ct);
        var notify = await _settings.Get(SettingsService.NotifyHoursBefore, "12", ct);
        var web = await _settings.Get(SettingsService.WebAdminUrl, "", ct);

        await _bot.SendMessage(chatId,
            $"⚙️ <b>Sozlamalar</b>\n\n" +
            $"💳 Karta:    <code>{card}</code>\n" +
            $"👤 Egasi:    <b>{owner}</b>\n" +
            $"📞 Support:  {sup}\n" +
            $"🔔 Eslatma:  {notify} soat\n" +
            $"🌐 Web URL:  <code>{web}</code>\n\n" +
            "O'zgartirish uchun tugmani bosing:",
            parseMode: ParseMode.Html,
            replyMarkup: AdminKeyboards.SettingsMenu(), cancellationToken: ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MAJBURIY OBUNA
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowMandatory(long chatId, CancellationToken ct)
    {
        var enabled = await _settings.GetBool(SettingsService.MandatoryEnabled, false, ct);
        var ids = await _settings.GetMandatoryChannelIds(ct);
        var chanList = ids.Count > 0 ? string.Join("\n", ids.Select(id => $"• <code>{id}</code>")) : "Hozircha kanal yo'q";

        await _bot.SendMessage(chatId,
            $"🔒 <b>Majburiy Obuna</b>\n\n" +
            $"Holat: {(enabled ? "🟢 Yoqilgan" : "🔴 O'chirilgan")}\n\n" +
            $"<b>Kanallar:</b>\n{chanList}",
            parseMode: ParseMode.Html,
            replyMarkup: AdminKeyboards.MandatoryMenu(enabled), cancellationToken: ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TRADER KANALLAR
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowTraderChannels(long chatId, CancellationToken ct)
    {
        var channels = await _db.TraderChannels.OrderBy(c => c.SortOrder).ToListAsync(ct);
        await _bot.SendMessage(chatId,
            channels.Count == 0 ? "📡 Trader kanallar yo'q." : $"📡 <b>Trader Kanallar ({channels.Count} ta)</b>",
            parseMode: ParseMode.Html,
            replyMarkup: new ReplyKeyboardMarkup(new[] { new[] { new KeyboardButton("➕ Yangi trader kanal"), new KeyboardButton("❌ Bekor qilish") } }) { ResizeKeyboard = true },
            cancellationToken: ct);
        foreach (var c in channels)
            await _bot.SendMessage(chatId,
                $"{(c.IsActive ? "🟢" : "🔴")} {c.Emoji} <b>{c.Title}</b>\n{c.Description}\n{c.Link}",
                parseMode: ParseMode.Html,
                replyMarkup: AdminKeyboards.TraderChActions(c.Id, c.IsActive), cancellationToken: ct);
    }

    private async Task StartAddTrCh(long chatId, AdminSession s, CancellationToken ct)
    {
        s.EditTrChId = null; s.Step = AdminStep.WaitTrChTitle;
        await Send(chatId, "📡 <b>Yangi Trader Kanal</b>\n\nKanal nomini kiriting:", AdminKeyboards.CancelMenu(), ct);
    }

    private async Task SaveTrCh(long chatId, AdminSession s, CancellationToken ct)
    {
        if (s.EditTrChId.HasValue)
        {
            var ch = await _db.TraderChannels.FindAsync(new object[] { s.EditTrChId.Value }, ct);
            if (ch is not null) { ch.Title = s.TrChTitle; ch.Emoji = s.TrChEmoji; ch.Description = s.TrChDesc; ch.Link = s.TrChLink; }
        }
        else _db.TraderChannels.Add(new TraderChannel { Title = s.TrChTitle, Emoji = s.TrChEmoji, Description = s.TrChDesc, Link = s.TrChLink, IsActive = true });
        await _db.SaveChangesAsync(ct); _state.Clear(chatId);
        await Send(chatId, $"✅ Kanal {(s.EditTrChId.HasValue ? "yangilandi" : "qo'shildi")}!\n\n{s.TrChEmoji} <b>{s.TrChTitle}</b>\n{s.TrChLink}", AdminKeyboards.MainMenu(), ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ADMINLAR
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowAdmins(long chatId, CancellationToken ct)
    {
        var admins = await _db.AdminUsers.ToListAsync(ct);

        await _bot.SendMessage(chatId,
            $"👑 <b>Adminlar</b>\n\n" +
            $"🔱 Super Admin: <code>{_cfg.AdminId}</code>\n\n" +
            (admins.Count > 0
                ? string.Join("\n", admins.Select(a => $"• <code>{a.TelegramId}</code> — {a.FullName}"))
                : "Qo'shimcha admin yo'q."),
            parseMode: ParseMode.Html,
            replyMarkup: new ReplyKeyboardMarkup(new[] { new[] { new KeyboardButton("➕ Admin qo'shish"), new KeyboardButton("❌ Bekor qilish") } }) { ResizeKeyboard = true },
            cancellationToken: ct);

        foreach (var a in admins)
            await _bot.SendMessage(chatId,
                $"👤 <b>{a.FullName}</b>\n🆔 <code>{a.TelegramId}</code>",
                parseMode: ParseMode.Html,
                replyMarkup: AdminKeyboards.AdminActions(a.Id), cancellationToken: ct);
    }

    private async Task StartAddAdmin(long chatId, AdminSession s, CancellationToken ct)
    {
        s.Step = AdminStep.WaitNewAdminId;
        await Send(chatId,
            "👑 <b>Yangi Admin Qo'shish</b>\n\n" +
            "Yangi adminning Telegram ID sini kiriting.\n" +
            "ID ni bilish uchun @userinfobot ga /start yuboring.",
            AdminKeyboards.CancelMenu(), ct);
    }

    private async Task AddAdmin(long chatId, long newAdminId, CancellationToken ct)
    {
        if (newAdminId == _cfg.AdminId)
        { await Send(chatId, "❗️ Bu Super Admin!", AdminKeyboards.MainMenu(), ct); return; }

        var existing = await _db.AdminUsers.FirstOrDefaultAsync(a => a.TelegramId == newAdminId, ct);
        if (existing is not null)
        { await Send(chatId, "❗️ Bu foydalanuvchi allaqachon admin.", AdminKeyboards.MainMenu(), ct); return; }

        // Foydalanuvchini botdan topamiz
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == newAdminId, ct);
        var name = user?.FullName ?? $"Admin {newAdminId}";

        _db.AdminUsers.Add(new AdminUser { TelegramId = newAdminId, FullName = name });
        await _db.SaveChangesAsync(ct);

        try { await _bot.SendMessage(newAdminId, "👑 <b>Tabriklaymiz! Siz admin qildingiz.</b>", parseMode: ParseMode.Html, cancellationToken: ct); } catch { }
        await Send(chatId, $"✅ <b>{name}</b> admin qilindi!\n🆔 <code>{newAdminId}</code>", AdminKeyboards.MainMenu(), ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  WEB PANEL
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowWebPanel(long chatId, CancellationToken ct)
    {
        var url = await _settings.Get(SettingsService.WebAdminUrl, "", ct);
        if (string.IsNullOrEmpty(url))
        { await Send(chatId, "❗️ Web panel URL sozlanmagan. ⚙️ Sozlamalardan kiriting.", AdminKeyboards.MainMenu(), ct); return; }

        await _bot.SendMessage(chatId,
            $"🌐 <b>Web Admin Panel</b>\n\n" +
            $"Quyidagi havoladan kirish mumkin:\n{url}\n\n" +
            "<i>Havola faqat bot ishlayotgan paytda ochiladi.</i>",
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithUrl("🌐 Web panelni ochish", url) }
            }), cancellationToken: ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MA'LUMOTLARNI TOZALASH
    // ══════════════════════════════════════════════════════════════════════
    private async Task ShowClearMenu(long chatId, CancellationToken ct)
    {
        await _bot.SendMessage(chatId,
            "🗑 <b>Ma'lumotlarni Tozalash</b>\n\n" +
            "⚠️ Bu amal qaytarib bo'lmaydi!\n\n" +
            "Nima o'chirmoqchisiz?",
            parseMode: ParseMode.Html,
            replyMarkup: AdminKeyboards.ClearDataMenu(), cancellationToken: ct);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  YORDAMCHI
    // ══════════════════════════════════════════════════════════════════════
    private async Task Send(long chatId, string text, object? markup, CancellationToken ct)
    {
        if (markup is InlineKeyboardMarkup inline)
            await _bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: inline, cancellationToken: ct);
        else if (markup is ReplyKeyboardMarkup reply)
            await _bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: reply, cancellationToken: ct);
        else
            await _bot.SendMessage(chatId, text, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private async Task<DateTime> CreateOrExtendSub(int userId, int planId, int days, CancellationToken ct)
    {
        var ex = await _db.Subscriptions.Where(s => s.UserId == userId && s.IsActive).FirstOrDefaultAsync(ct);
        if (ex is not null && ex.ExpireDate > DateTime.UtcNow)
        { ex.ExpireDate = ex.ExpireDate.AddDays(days); ex.Notified = false; await _db.SaveChangesAsync(ct); return ex.ExpireDate; }
        var sub = new Subscription { UserId = userId, PlanId = planId, StartDate = DateTime.UtcNow, ExpireDate = DateTime.UtcNow.AddDays(days), IsActive = true };
        _db.Subscriptions.Add(sub); await _db.SaveChangesAsync(ct); return sub.ExpireDate;
    }
}