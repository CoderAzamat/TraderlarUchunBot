using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TradingBotVIPv3.Bot;
using TradingBotVIPv3.Config;
using TradingBotVIPv3.Data;
using TradingBotVIPv3.Data.Models;
using TradingBotVIPv3.Helpers;
using TradingBotVIPv3.Services;
using TBUser = TradingBotVIPv3.Data.Models.User;

namespace TradingBotVIPv3.Bot;

public sealed class UpdateHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly AppDbContext _db;
    private readonly BotConfig _cfg;
    private readonly AdminHandler _admin;
    private readonly SettingsService _settings;
    private readonly AdminStateService _state;

    public UpdateHandler(ITelegramBotClient bot, AppDbContext db, BotConfig cfg,
                         AdminHandler admin, SettingsService settings, AdminStateService state)
    {
        _bot = bot; _db = db; _cfg = cfg;
        _admin = admin; _settings = settings; _state = state;
    }

    // ── Asosiy kirish ──────────────────────────────────────────────────────
    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        try
        {
            switch (update.Type)
            {
                case UpdateType.Message when update.Message is not null:
                    await OnMessage(update.Message, ct); break;
                case UpdateType.CallbackQuery when update.CallbackQuery is not null:
                    await OnCallback(update.CallbackQuery, ct); break;
            }
        }
        catch (Exception ex) { Console.WriteLine($"[ERROR] {ex.Message}"); }
    }

    private bool IsAdmin(long fromId) =>
        fromId == _cfg.AdminId || _db.AdminUsers.Any(a => a.TelegramId == fromId);

    // ── Message ────────────────────────────────────────────────────────────
    private async Task OnMessage(Message msg, CancellationToken ct)
    {
        var fromId = msg.From?.Id ?? 0;
        var chatId = msg.Chat.Id;
        if (fromId == 0) return;

        if (msg.Contact is not null) { await OnContact(msg.Contact, fromId, chatId, ct); return; }
        if (msg.Photo is not null && msg.Photo.Length > 0) { await OnPhoto(msg, fromId, chatId, ct); return; }
        if (msg.Text is null) return;

        var text = msg.Text.Trim();
        if (text == "/start") { await OnStart(fromId, msg.From!.FirstName ?? "User", chatId, ct); return; }

        if (IsAdmin(fromId))
        {
            if (text == "🏠 Asosiy menyu")
            {
                // Admin menyudan user menyusiga
                await _bot.SendMessage(chatId, "🏠 Asosiy menyu",
                    replyMarkup: Keyboards.MainMenu(), cancellationToken: ct);
                return;
            }
            if (text == "👤 Admin panel" || text == "👑 Admin panel")
            {
                await _bot.SendMessage(chatId, "👑 Admin panel",
                    replyMarkup: AdminKeyboards.MainMenu(), cancellationToken: ct);
                return;
            }
            if (await _admin.HandleText(text, chatId, ct)) return;
        }

        var dbUser = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == fromId, ct);
        if (dbUser is null) { await OnStart(fromId, msg.From!.FirstName ?? "User", chatId, ct); return; }
        if (dbUser.IsBanned) { await _bot.SendMessage(chatId, "🚫 Siz bloklangansiz.", cancellationToken: ct); return; }

        switch (dbUser.UserStep)
        {
            case "WaitingForName": await OnNameInput(dbUser, text, chatId, ct); break;
            case "WaitingForPhone":
                await _bot.SendMessage(chatId, "📱 Tugma orqali telefon yuboring:",
                    replyMarkup: Keyboards.RequestPhone(), cancellationToken: ct); break;
            case "WaitingForTopup": await OnTopupInput(dbUser, text, chatId, ct); break;
            case "Active": await OnMainMenu(dbUser, text, chatId, ct); break;
        }
    }

    // ── /start ─────────────────────────────────────────────────────────────
    private async Task OnStart(long fromId, string firstName, long chatId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == fromId, ct);
        if (user is null)
        {
            user = new TBUser { TelegramId = fromId, FullName = firstName, UserStep = "WaitingForName" };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }

        if (user.IsBanned) { await _bot.SendMessage(chatId, "🚫 Siz bloklangansiz.", cancellationToken: ct); return; }

        if (user.UserStep == "Active")
        {
            // Majburiy obuna tekshirish — BARCHA userlarda
            if (await CheckMandatory(fromId, chatId, ct)) return;

            if (IsAdmin(fromId))
                await _bot.SendMessage(chatId,
                    $"👑 <b>Admin Panel</b>\n\nXush kelibsiz, {user.FullName}!",
                    parseMode: ParseMode.Html, replyMarkup: AdminKeyboards.MainMenu(), cancellationToken: ct);
            else
                await _bot.SendMessage(chatId,
                    $"👋 Xush kelibsiz, <b>{user.FullName}</b>!\n💰 Balans: <b>{user.Balance:N0} UZS</b>",
                    parseMode: ParseMode.Html, replyMarkup: Keyboards.MainMenu(), cancellationToken: ct);
            return;
        }

        user.UserStep = "WaitingForName";
        await _db.SaveChangesAsync(ct);

        var welcome = await _settings.Get(SettingsService.WelcomeMessage,
            "👋 <b>VIP Bot ga xush kelibsiz!</b>\n\n📝 Ismingizni kiriting:", ct);
        await _bot.SendMessage(chatId, welcome, parseMode: ParseMode.Html,
            replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
    }

    // ── Majburiy obuna ─────────────────────────────────────────────────────
    private async Task<bool> CheckMandatory(long fromId, long chatId, CancellationToken ct)
    {
        if (IsAdmin(fromId)) return false;
        var enabled = await _settings.GetBool(SettingsService.MandatoryEnabled, false, ct);
        if (!enabled) return false;

        var channelIds = await _settings.GetMandatoryChannelIds(ct);
        if (channelIds.Count == 0) return false;

        var notSub = new List<(long Id, string Title, string Link)>();
        foreach (var cid in channelIds)
        {
            try
            {
                var member = await _bot.GetChatMember(cid, fromId, cancellationToken: ct);
                if (member.Status is ChatMemberStatus.Left or ChatMemberStatus.Kicked)
                {
                    var chat = await _bot.GetChat(cid, cancellationToken: ct);
                    notSub.Add((cid, chat.Title ?? "Kanal", chat.InviteLink ?? ""));
                }
            }
            catch
            {
                notSub.Add((cid, $"Kanal {cid}", ""));
            }
        }

        if (notSub.Count == 0) return false;

        var buttons = notSub
            .Where(x => !string.IsNullOrEmpty(x.Link))
            .Select(x => new[] { InlineKeyboardButton.WithUrl($"📢 {x.Title}", x.Link) })
            .ToList();
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("✅ Obuna bo'ldim, tekshirish", "check_subscribe") });

        await _bot.SendMessage(chatId,
            "⚠️ <b>Botdan foydalanish uchun quyidagi kanallarga obuna bo'ling:</b>",
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        return true;
    }

    // ── Ism ────────────────────────────────────────────────────────────────
    private async Task OnNameInput(TBUser user, string text, long chatId, CancellationToken ct)
    {
        if (text.Length < 2) { await _bot.SendMessage(chatId, "❗️ Kamida 2 ta belgi kiriting:", cancellationToken: ct); return; }
        user.FullName = text; user.UserStep = "WaitingForPhone";
        await _db.SaveChangesAsync(ct);
        await _bot.SendMessage(chatId,
            $"✅ <b>{user.FullName}</b>!\n\n📱 Telefon raqamingizni tasdiqlang 👇",
            parseMode: ParseMode.Html, replyMarkup: Keyboards.RequestPhone(), cancellationToken: ct);
    }

    // ── Telefon ────────────────────────────────────────────────────────────
    private async Task OnContact(Contact contact, long fromId, long chatId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == fromId, ct);
        if (user is null || user.UserStep != "WaitingForPhone") return;

        user.PhoneNumber = contact.PhoneNumber; user.UserStep = "Active";
        await _db.SaveChangesAsync(ct);

        if (await CheckMandatory(fromId, chatId, ct)) return;

        var menu = IsAdmin(fromId) ? (ReplyKeyboardMarkup)AdminKeyboards.MainMenu() : Keyboards.MainMenu();
        await _bot.SendMessage(chatId,
            $"🎉 <b>Ro'yxatdan o'tdingiz!</b>\n\n👤 <b>{user.FullName}</b>\n📱 <code>{user.PhoneNumber}</code>",
            parseMode: ParseMode.Html, replyMarkup: menu, cancellationToken: ct);
    }

    // ── Asosiy menyu ───────────────────────────────────────────────────────
    private async Task OnMainMenu(TBUser user, string text, long chatId, CancellationToken ct)
    {
        if (await CheckMandatory(user.TelegramId, chatId, ct)) return;

        switch (text)
        {
            case "⭐️ VIP Obuna": await ShowPlans(user, chatId, ct); break;
            case "📋 Mening obunalarim": await ShowMySubscriptions(user, chatId, ct); break;
            case "💰 Hisob": await ShowProfile(user, chatId, ct); break;
            case "📢 Kanallar": await ShowTraderChannels(chatId, ct); break;
            case "📞 Support":
                var support = await _settings.Get(SettingsService.SupportUsername, "@support", ct);
                await _bot.SendMessage(chatId, $"📞 <b>Murojaat:</b>\n\n{support}", parseMode: ParseMode.Html, cancellationToken: ct);
                break;
            case "ℹ️ Bot haqida":
                var about = await _settings.Get(SettingsService.BotAboutText, "VIP Bot", ct);
                var sup = await _settings.Get(SettingsService.SupportUsername, "@support", ct);
                await _bot.SendMessage(chatId, $"ℹ️ <b>Bot haqida</b>\n\n{about}\n\n📞 {sup}", parseMode: ParseMode.Html, cancellationToken: ct);
                break;
        }
    }

    // ── Trader kanallari ───────────────────────────────────────────────────
    private async Task ShowTraderChannels(long chatId, CancellationToken ct)
    {
        var channels = await _db.TraderChannels
            .Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync(ct);

        if (channels.Count == 0)
        {
            await _bot.SendMessage(chatId, "📢 Hozircha kanallar yo'q.", cancellationToken: ct);
            return;
        }

        await _bot.SendMessage(chatId,
            "📢 <b>Bizning Kanallar</b>\n\nQuyidagi kanallarga obuna bo'ling 👇",
            parseMode: ParseMode.Html,
            replyMarkup: Keyboards.TraderChannels(channels), cancellationToken: ct);
    }

    // ── VIP Rejalar ────────────────────────────────────────────────────────
    private async Task ShowPlans(TBUser user, long chatId, CancellationToken ct)
    {
        var plans = await _db.Plans.Include(p => p.VipChannel)
            .Where(p => p.IsActive).OrderBy(p => p.Price).ToListAsync(ct);

        if (plans.Count == 0)
        { await _bot.SendMessage(chatId, "❌ Hozircha faol rejalar yo'q.", cancellationToken: ct); return; }

        var activeSub = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == user.Id && s.IsActive && s.ExpireDate > DateTime.UtcNow)
            .FirstOrDefaultAsync(ct);

        var subLine = activeSub is not null
            ? $"\n\n📌 <b>Faol:</b> {activeSub.Plan.Title} — {activeSub.ExpireDate.TashkentDate()} gacha"
            : string.Empty;

        await _bot.SendMessage(chatId,
            $"⭐️ <b>VIP Rejalar</b>{subLine}\n\n💰 Balans: <b>{user.Balance:N0} UZS</b>\n\nRejakni tanlang 👇",
            parseMode: ParseMode.Html, replyMarkup: Keyboards.Plans(plans), cancellationToken: ct);
    }

    // ── Mening obunalarim ──────────────────────────────────────────────────
    private async Task ShowMySubscriptions(TBUser user, long chatId, CancellationToken ct)
    {
        var subs = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == user.Id).OrderByDescending(s => s.CreatedAt).Take(10).ToListAsync(ct);

        if (subs.Count == 0)
        {
            await _bot.SendMessage(chatId,
                "📋 <b>Sizda hozircha obuna yo'q.</b>\n\n⭐️ VIP Obuna bo'limidan reja tanlang!",
                parseMode: ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(new[]
                { new[] { InlineKeyboardButton.WithCallbackData("⭐️ Obuna olish", "plans_menu") } }),
                cancellationToken: ct);
            return;
        }

        var active = subs.FirstOrDefault(s => s.IsActive && s.ExpireDate > DateTime.UtcNow);
        var txt = "📋 <b>Mening Obunalarim</b>\n\n";

        if (active is not null)
        {
            var left = (int)(active.ExpireDate - DateTime.UtcNow).TotalDays;
            txt += $"✅ <b>Faol obuna:</b>\n" +
                   $"⭐️ {active.Plan.Title}\n" +
                   $"📅 {active.StartDate.TashkentDate()} — {active.ExpireDate.TashkentDate()}\n" +
                   $"⏰ <b>{left} kun qoldi</b>\n\n";
        }

        txt += "<b>Tarixi:</b>\n";
        foreach (var s in subs.Take(5))
        {
            var st = s.IsActive && s.ExpireDate > DateTime.UtcNow ? "✅" : "⛔";
            txt += $"{st} {s.Plan.Title} — {s.StartDate.TashkentDate()}\n";
        }

        var markup = active is not null
            ? new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔄 Uzaytirish", "plans_menu") } })
            : new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("⭐️ Obuna olish", "plans_menu") } });

        await _bot.SendMessage(chatId, txt, parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
    }

    // ── Profil ─────────────────────────────────────────────────────────────
    private async Task ShowProfile(TBUser user, long chatId, CancellationToken ct)
    {
        var sub = await _db.Subscriptions.Include(s => s.Plan)
            .Where(s => s.UserId == user.Id && s.IsActive && s.ExpireDate > DateTime.UtcNow)
            .FirstOrDefaultAsync(ct);

        var subLine = sub is not null
            ? $"⭐️ VIP: <b>{sub.Plan.Title}</b>\n⏰ {sub.ExpireDate.TashkentDate()} gacha"
            : "⭐️ VIP: <b>Yo'q</b>";

        await _bot.SendMessage(chatId,
            $"👤 <b>Mening Hisobim</b>\n\n" +
            $"👤 Ism:    <b>{user.FullName}</b>\n" +
            $"📱 Raqam:  <code>{user.PhoneNumber}</code>\n" +
            $"💰 Balans: <b>{user.Balance:N0} UZS</b>\n" +
            $"{subLine}\n" +
            $"🆔 ID: <code>{user.Id}</code>",
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("💳 Hisob to'ldirish", "topup_menu") },
                new[] { InlineKeyboardButton.WithCallbackData("📋 Obunalarim",       "my_subs")    }
            }), cancellationToken: ct);
    }

    // ── Hisob to'ldirish ───────────────────────────────────────────────────
    private async Task OnTopupInput(TBUser user, string text, long chatId, CancellationToken ct)
    {
        if (text == "❌ Bekor qilish")
        {
            user.UserStep = "Active"; await _db.SaveChangesAsync(ct);
            await _bot.SendMessage(chatId, "❌ Bekor qilindi.",
                replyMarkup: Keyboards.MainMenu(), cancellationToken: ct);
            return;
        }

        var clean = text.Replace(" ", "").Replace(",", "").Replace(".", "");
        if (!decimal.TryParse(clean, out var amount) || amount < 1000)
        {
            await _bot.SendMessage(chatId,
                "❗️ Kamida <b>1 000 UZS</b> kiriting.\nMasalan: <code>500000</code>",
                parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        var payment = new Payment { UserId = user.Id, Amount = amount, Type = PaymentType.TopUp };
        _db.Payments.Add(payment);
        user.UserStep = "Active";
        await _db.SaveChangesAsync(ct);

        var card = await _settings.Get(SettingsService.CardNumber, _cfg.CardNumber, ct);
        var owner = await _settings.Get(SettingsService.CardOwner, _cfg.CardOwner, ct);

        await _bot.SendMessage(chatId,
            $"💳 <b>Hisob To'ldirish</b>\n\n" +
            $"💰 Summa: <b>{amount:N0} UZS</b>\n\n" +
            $"💳 Karta: <code>{card}</code>\n" +
            $"👤 Egasi: <b>{owner}</b>\n\n" +
            "Kartaga o'tkazib, <b>chek rasmini</b> yuboring 📸",
            parseMode: ParseMode.Html, replyMarkup: Keyboards.MainMenu(), cancellationToken: ct);
    }

    // ── Rasm (chek) ────────────────────────────────────────────────────────
    private async Task OnPhoto(Message msg, long fromId, long chatId, CancellationToken ct)
    {
        if (IsAdmin(fromId)) return;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == fromId, ct);
        if (user is null || user.UserStep != "Active") return;

        var payment = await _db.Payments.Include(p => p.Plan)
            .Where(p => p.UserId == user.Id && p.Status == PaymentStatus.Pending && p.ReceiptFileId == string.Empty)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);

        if (payment is null)
        {
            await _bot.SendMessage(chatId,
                "❓ Faol to'lov topilmadi.\n\n⭐️ VIP Obuna bo'limidan reja tanlang.", cancellationToken: ct);
            return;
        }

        var photos = msg.Photo!;
        var fileId = photos[photos.Length - 1].FileId;
        payment.ReceiptFileId = fileId;
        await _db.SaveChangesAsync(ct);

        var caption =
            $"💳 <b>Yangi to'lov #{payment.Id}</b>\n\n" +
            $"👤 {user.FullName}\n📱 {user.PhoneNumber}\n" +
            $"🆔 UserID: <code>{user.Id}</code>\n" +
            $"⭐️ {payment.Plan?.Title ?? "Hisob to'ldirish"}\n" +
            $"💰 <b>{payment.Amount:N0} UZS</b>\n" +
            $"🕐 {DateTime.UtcNow.TashkentFull()}";

        var adminMsg = await _bot.SendPhoto(_cfg.AdminId,
            InputFile.FromFileId(fileId), caption: caption, parseMode: ParseMode.Html,
            replyMarkup: AdminKeyboards.PaymentActions(payment.Id), cancellationToken: ct);

        payment.AdminMessageId = adminMsg.MessageId;
        await _db.SaveChangesAsync(ct);

        await _bot.SendMessage(chatId,
            "✅ <b>Chekingiz yuborildi!</b>\n\nAdmin 10–30 daqiqada tekshiradi.",
            parseMode: ParseMode.Html, cancellationToken: ct);
    }

    // ── Callback ───────────────────────────────────────────────────────────
    private async Task OnCallback(CallbackQuery cb, CancellationToken ct)
    {
        var fromId = cb.From.Id;
        var chatId = cb.Message!.Chat.Id;
        var data = cb.Data ?? "";

        await _bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);

        if (IsAdmin(fromId) && await _admin.HandleCallback(cb, ct)) return;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == fromId, ct);
        if (user is null) return;

        if (data == "check_subscribe")
        {
            if (!await CheckMandatory(fromId, chatId, ct))
                await _bot.SendMessage(chatId, "✅ <b>Rahmat! Botdan foydalanishingiz mumkin.</b>",
                    parseMode: ParseMode.Html, replyMarkup: Keyboards.MainMenu(), cancellationToken: ct);
            return;
        }

        if (data.StartsWith("plan:"))
        { await OnPlanSelected(user, int.Parse(data.Substring(5)), chatId, ct); return; }

        if (data.StartsWith("pay_balance:"))
        { await PayFromBalance(user, int.Parse(data.Substring(12)), chatId, cb.Message.MessageId, ct); return; }

        if (data.StartsWith("pay_card:"))
        {
            var p = await _db.Payments.Include(x => x.Plan).FirstOrDefaultAsync(x => x.Id == int.Parse(data.Substring(9)), ct);
            if (p is null) return;
            var card = await _settings.Get(SettingsService.CardNumber, _cfg.CardNumber, ct);
            var owner = await _settings.Get(SettingsService.CardOwner, _cfg.CardOwner, ct);
            await _bot.SendMessage(chatId,
                $"💳 <b>Karta orqali to'lash</b>\n\n💰 <b>{p.Amount:N0} UZS</b>\n" +
                $"💳 <code>{card}</code>\n👤 <b>{owner}</b>\n\nChek rasmini yuboring 📸",
                parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        if (data == "topup_menu")
        {
            var card = await _settings.Get(SettingsService.CardNumber, _cfg.CardNumber, ct);
            var owner = await _settings.Get(SettingsService.CardOwner, _cfg.CardOwner, ct);
            user.UserStep = "WaitingForTopup"; await _db.SaveChangesAsync(ct);
            await _bot.SendMessage(chatId,
                $"💳 <b>Hisob To'ldirish</b>\n\n" +
                $"💳 Karta: <code>{card}</code>\n👤 Egasi: <b>{owner}</b>\n\n" +
                "💰 Qancha pul kiritmoqchisiz?\n<i>Summani yozing (masalan: 500000)</i>",
                parseMode: ParseMode.Html, replyMarkup: Keyboards.CancelMenu(), cancellationToken: ct);
            return;
        }

        if (data == "my_subs") { await ShowMySubscriptions(user, chatId, ct); return; }
        if (data == "plans_menu") { await ShowPlans(user, chatId, ct); return; }

        if (data.StartsWith("cancel_payment:") || data == "cancel")
        {
            if (data.StartsWith("cancel_payment:"))
            {
                var p = await _db.Payments.FirstOrDefaultAsync(x => x.Id == int.Parse(data.Substring(15)), ct);
                if (p is not null && p.Status == PaymentStatus.Pending) { p.Status = PaymentStatus.Rejected; await _db.SaveChangesAsync(ct); }
            }
            try { await _bot.EditMessageText(chatId, cb.Message.MessageId, "❌ Bekor qilindi.", cancellationToken: ct); } catch { }
        }
    }

    // ── Reja tanlash ───────────────────────────────────────────────────────
    private async Task OnPlanSelected(TBUser user, int planId, long chatId, CancellationToken ct)
    {
        var plan = await _db.Plans.Include(p => p.VipChannel)
            .FirstOrDefaultAsync(p => p.Id == planId && p.IsActive, ct);
        if (plan is null) return;

        var old = await _db.Payments
            .Where(p => p.UserId == user.Id && p.Status == PaymentStatus.Pending && p.PlanId == planId)
            .ToListAsync(ct);
        foreach (var o in old) o.Status = PaymentStatus.Rejected;

        var payment = new Payment { UserId = user.Id, PlanId = planId, Amount = plan.Price, Type = PaymentType.Subscription };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        var hasBalance = user.Balance >= plan.Price;
        var card = await _settings.Get(SettingsService.CardNumber, _cfg.CardNumber, ct);
        var owner = await _settings.Get(SettingsService.CardOwner, _cfg.CardOwner, ct);

        var balText = hasBalance
            ? $"✅ <b>Balansingiz yetarli</b> ({user.Balance:N0} UZS)"
            : $"❗️ Balansingiz: <b>{user.Balance:N0} UZS</b>\nKerak: <b>{plan.Price:N0} UZS</b>\n\n💳 <code>{card}</code>\n👤 <b>{owner}</b>";

        await _bot.SendMessage(chatId,
            $"⭐️ <b>{plan.Title}</b> | 📺 {plan.VipChannel.Title}\n" +
            $"💰 <b>{plan.Price:N0} UZS</b> | 📅 {plan.DurationDays} kun\n\n{balText}",
            parseMode: ParseMode.Html,
            replyMarkup: Keyboards.PaymentChoice(payment.Id, hasBalance), cancellationToken: ct);
    }

    // ── Balansdan to'lash ──────────────────────────────────────────────────
    private async Task PayFromBalance(TBUser user, int paymentId, long chatId, int msgId, CancellationToken ct)
    {
        var payment = await _db.Payments
            .Include(p => p.Plan).Include(p => p.Plan!.VipChannel)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment is null || payment.Status != PaymentStatus.Pending) return;

        if (user.Balance < payment.Amount)
        {
            await _bot.SendMessage(chatId,
                $"❗️ Balansingiz yetarli emas.\nKerak: <b>{payment.Amount:N0}</b>, Mavjud: <b>{user.Balance:N0}</b> UZS",
                parseMode: ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(new[]
                { new[] { InlineKeyboardButton.WithCallbackData("💳 Hisob to'ldirish", "topup_menu") } }),
                cancellationToken: ct);
            return;
        }

        user.Balance -= payment.Amount; payment.Status = PaymentStatus.Approved;
        await _db.SaveChangesAsync(ct);
        var expDate = await CreateOrExtendSub(user.Id, payment.PlanId!.Value, payment.Plan!.DurationDays, ct);

        string inv;
        try
        {
            var link = await _bot.CreateChatInviteLink(payment.Plan.VipChannel.TelegramChannelId,
                memberLimit: 1, expireDate: DateTime.UtcNow.AddDays(1), cancellationToken: ct);
            inv = $"\n\n🔗 <b>VIP havola:</b>\n{link.InviteLink}\n\n⚠️ 1 marta ishlaydi!";
        }
        catch { inv = ""; }

        try
        {
            await _bot.EditMessageText(chatId, msgId,
                $"✅ <b>VIP faollashtirildi!</b>\n⭐️ {payment.Plan.Title} | 📅 {expDate.TashkentDate()} gacha\n💰 Balans: <b>{user.Balance:N0} UZS</b>{inv}",
                parseMode: ParseMode.Html, cancellationToken: ct);
        }
        catch
        {
            await _bot.SendMessage(chatId,
                $"✅ <b>VIP faollashtirildi!</b>\n⭐️ {payment.Plan.Title} | 📅 {expDate.TashkentDate()} gacha{inv}",
                parseMode: ParseMode.Html, cancellationToken: ct);
        }
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