using Telegram.Bot.Types.ReplyMarkups;
using TradingBotVIPv3.Data.Models;

namespace TradingBotVIPv3.Bot;

public static class Keyboards
{
    public static ReplyKeyboardMarkup RequestPhone() =>
        new(new[] { new[] { KeyboardButton.WithRequestContact("📱 Telefon raqamni yuborish") } })
        { ResizeKeyboard = true, OneTimeKeyboard = true };

    public static ReplyKeyboardMarkup MainMenu() =>
        new(new[]
        {
            new[] { new KeyboardButton("⭐️ VIP Obuna"),       new KeyboardButton("📋 Mening obunalarim") },
            new[] { new KeyboardButton("💰 Hisob"),            new KeyboardButton("📢 Kanallar")          },
            new[] { new KeyboardButton("📞 Support"),          new KeyboardButton("ℹ️ Bot haqida")        }
        })
        { ResizeKeyboard = true };

    public static ReplyKeyboardMarkup CancelMenu() =>
        new(new[] { new[] { new KeyboardButton("❌ Bekor qilish") } })
        { ResizeKeyboard = true };

    public static InlineKeyboardMarkup ProfileMenu() =>
        new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("💳 Hisob to'ldirish", "topup_menu") },
            new[] { InlineKeyboardButton.WithCallbackData("📋 Obunalarim",       "my_subs")    }
        });

    public static InlineKeyboardMarkup Plans(List<Plan> plans)
    {
        var rows = plans.Select(p =>
            new[] { InlineKeyboardButton.WithCallbackData(
                $"⭐️ {p.Title} — {p.Price:N0} UZS ({p.DurationDays} kun)",
                $"plan:{p.Id}") }).ToList();
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup PaymentChoice(int paymentId, bool hasBalance) =>
        hasBalance
            ? new InlineKeyboardMarkup(new[]
              {
                  new[] { InlineKeyboardButton.WithCallbackData("💰 Balansdan to'lash",    $"pay_balance:{paymentId}") },
                  new[] { InlineKeyboardButton.WithCallbackData("💳 Karta orqali to'lash", $"pay_card:{paymentId}")   },
                  new[] { InlineKeyboardButton.WithCallbackData("❌ Bekor",                $"cancel_payment:{paymentId}") }
              })
            : new InlineKeyboardMarkup(new[]
              {
                  new[] { InlineKeyboardButton.WithCallbackData("💳 Karta orqali to'lash", $"pay_card:{paymentId}")   },
                  new[] { InlineKeyboardButton.WithCallbackData("💰 Hisobni to'ldirish",   "topup_menu")              },
                  new[] { InlineKeyboardButton.WithCallbackData("❌ Bekor",                $"cancel_payment:{paymentId}") }
              });

    public static InlineKeyboardMarkup RenewReminder() =>
        new(new[] { new[] { InlineKeyboardButton.WithCallbackData("🔄 Uzaytirish", "plans_menu") } });

    // Trader kanallari (URL tugmalar)
    public static InlineKeyboardMarkup TraderChannels(List<TraderChannel> channels)
    {
        var rows = channels
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new[] { InlineKeyboardButton.WithUrl($"{c.Emoji} {c.Title}", c.Link) })
            .ToList();
        return new InlineKeyboardMarkup(rows);
    }
}