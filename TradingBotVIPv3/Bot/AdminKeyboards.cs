using Telegram.Bot.Types.ReplyMarkups;
using TradingBotVIPv3.Data.Models;

namespace TradingBotVIPv3.Bot;

public static class AdminKeyboards
{
    public static ReplyKeyboardMarkup MainMenu() =>
        new(new[]
        {
            new[] { new KeyboardButton("📊 Statistika"),        new KeyboardButton("👥 Foydalanuvchilar") },
            new[] { new KeyboardButton("💳 To'lovlar"),         new KeyboardButton("⭐️ Obunalar")        },
            new[] { new KeyboardButton("📺 VIP Kanallar"),      new KeyboardButton("📋 Rejalar")          },
            new[] { new KeyboardButton("📢 Xabar yuborish"),    new KeyboardButton("⚙️ Sozlamalar")      },
            new[] { new KeyboardButton("🔒 Majburiy obuna"),    new KeyboardButton("📡 Trader kanallar") },
            new[] { new KeyboardButton("👑 Adminlar"),          new KeyboardButton("🌐 Web panel")       },
            new[] { new KeyboardButton("🗑 Ma'lumotlarni tozalash")                                     },
            new[] { new KeyboardButton("🏠 Asosiy menyu") }
        })
        { ResizeKeyboard = true };

    public static ReplyKeyboardMarkup CancelMenu() =>
        new(new[] { new[] { new KeyboardButton("❌ Bekor qilish") } })
        { ResizeKeyboard = true };

    public static InlineKeyboardMarkup PaymentActions(int paymentId) =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Tasdiqlash", $"adm_approve:{paymentId}"),
                InlineKeyboardButton.WithCallbackData("❌ Rad etish",  $"adm_reject:{paymentId}")
            },
            new[] { InlineKeyboardButton.WithCallbackData("👤 Foydalanuvchini ko'rish", $"adm_viewuser_pay:{paymentId}") }
        });

    public static InlineKeyboardMarkup UserActions(int userId, bool isBanned) =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("💰 Balans",     $"adm_addbal:{userId}"),
                InlineKeyboardButton.WithCallbackData("✉️ Xabar",      $"adm_msg:{userId}")
            },
            new[]
            {
                isBanned
                    ? InlineKeyboardButton.WithCallbackData("🔓 Blokdan chiqar", $"adm_unban:{userId}")
                    : InlineKeyboardButton.WithCallbackData("🚫 Bloklash",       $"adm_ban:{userId}"),
                InlineKeyboardButton.WithCallbackData("⭐️ VIP bering", $"adm_givevip:{userId}")
            },
            new[] { InlineKeyboardButton.WithCallbackData("👑 Admin qilish", $"adm_makeadmin:{userId}") }
        });

    public static InlineKeyboardMarkup ChannelActions(int channelId, bool isActive) =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✏️ Tahrirlash", $"adm_editchan:{channelId}"),
                isActive
                    ? InlineKeyboardButton.WithCallbackData("🔴 O'chirish", $"adm_deactivechan:{channelId}")
                    : InlineKeyboardButton.WithCallbackData("🟢 Yoqish",    $"adm_activechan:{channelId}")
            },
            new[] { InlineKeyboardButton.WithCallbackData("🗑 O'chirish", $"adm_delchan:{channelId}") }
        });

    public static InlineKeyboardMarkup PlanActions(int planId, bool isActive) =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✏️ Tahrirlash", $"adm_editplan:{planId}"),
                isActive
                    ? InlineKeyboardButton.WithCallbackData("🔴 Nofaol", $"adm_deactiveplan:{planId}")
                    : InlineKeyboardButton.WithCallbackData("🟢 Faol",   $"adm_activeplan:{planId}")
            }
        });

    public static InlineKeyboardMarkup ChannelSelect(List<VipChannel> channels) =>
        new(channels.Select(c =>
            new[] { InlineKeyboardButton.WithCallbackData(
                $"{(c.IsActive ? "🟢" : "🔴")} {c.Title}", $"adm_planchan:{c.Id}") }).ToList());

    public static InlineKeyboardMarkup GiveVipPlans(List<Plan> plans, int userId) =>
        new(plans.Select(p =>
            new[] { InlineKeyboardButton.WithCallbackData(
                $"⭐️ {p.Title} ({p.DurationDays} kun)", $"adm_givevip_plan:{userId}:{p.Id}") }).ToList());

    public static InlineKeyboardMarkup SettingsMenu() =>
        new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("💳 Karta raqami",    "adm_set:CardNumber")        },
            new[] { InlineKeyboardButton.WithCallbackData("👤 Karta egasi",     "adm_set:CardOwner")         },
            new[] { InlineKeyboardButton.WithCallbackData("📞 Support",         "adm_set:SupportUsername")   },
            new[] { InlineKeyboardButton.WithCallbackData("👋 Salomlashuv",     "adm_set:WelcomeMessage")    },
            new[] { InlineKeyboardButton.WithCallbackData("ℹ️ Bot haqida",      "adm_set:BotAboutText")      },
            new[] { InlineKeyboardButton.WithCallbackData("🔔 Eslatma vaqti",   "adm_set:NotifyHoursBefore") },
            new[] { InlineKeyboardButton.WithCallbackData("🌐 Web panel URL",   "adm_set:WebAdminUrl")       },
        });

    public static InlineKeyboardMarkup MandatoryMenu(bool enabled) =>
        new(new[]
        {
            new[] {
                enabled
                    ? InlineKeyboardButton.WithCallbackData("🔴 O'chirish",          "adm_mandatory_off")
                    : InlineKeyboardButton.WithCallbackData("🟢 Yoqish",             "adm_mandatory_on")
            },
            new[] { InlineKeyboardButton.WithCallbackData("➕ Kanal qo'shish",       "adm_mandatory_add")   },
            new[] { InlineKeyboardButton.WithCallbackData("🗑 Kanallarni tozalash",  "adm_mandatory_clear") }
        });

    public static InlineKeyboardMarkup TraderChActions(int id, bool isActive) =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✏️ Tahrirlash", $"adm_edittrch:{id}"),
                isActive
                    ? InlineKeyboardButton.WithCallbackData("🔴 Nofaol", $"adm_deactivetrch:{id}")
                    : InlineKeyboardButton.WithCallbackData("🟢 Faol",   $"adm_activetrch:{id}")
            },
            new[] { InlineKeyboardButton.WithCallbackData("🗑 O'chirish", $"adm_deltrch:{id}") }
        });

    public static InlineKeyboardMarkup AdminActions(int adminUserId) =>
        new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🗑 O'chirish", $"adm_deladmin:{adminUserId}") }
        });

    public static InlineKeyboardMarkup ClearDataMenu() =>
        new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🗑 Barcha foydalanuvchilarni o'chirish", "adm_clear_users")    },
            new[] { InlineKeyboardButton.WithCallbackData("🗑 Barcha to'lovlarni o'chirish",        "adm_clear_payments") },
            new[] { InlineKeyboardButton.WithCallbackData("🗑 Barcha obunalarni o'chirish",         "adm_clear_subs")     },
            new[] { InlineKeyboardButton.WithCallbackData("⚠️ HAMMASINI O'CHIRISH",               "adm_clear_all")      },
            new[] { InlineKeyboardButton.WithCallbackData("❌ Bekor",                              "adm_cancel")         }
        });

    public static InlineKeyboardMarkup Confirm(string yesData, string text = "✅ Ha") =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(text,      yesData),
                InlineKeyboardButton.WithCallbackData("❌ Yo'q", "adm_cancel")
            }
        });
}