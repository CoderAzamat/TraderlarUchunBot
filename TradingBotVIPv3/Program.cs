using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TradingBotVIPv3.Bot;
using TradingBotVIPv3.Config;
using TradingBotVIPv3.Data;
using TradingBotVIPv3.Jobs;
using TradingBotVIPv3.Services;

var builder = WebApplication.CreateBuilder(args);

var cfg = builder.Configuration.GetSection("BotConfig").Get<BotConfig>()
    ?? throw new InvalidOperationException("BotConfig topilmadi!");
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection topilmadi!");

builder.Services.AddSingleton(cfg);

builder.Services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(connStr));

builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(cfg.Token));

builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<PdfExportService>();

builder.Services.AddSingleton<AdminStateService>();
builder.Services.AddScoped<AdminHandler>();
builder.Services.AddScoped<UpdateHandler>();

builder.Services.AddHostedService<ExpireSubscriptionsJob>();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ── Database ────────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();

    db.Database.EnsureCreated();

    var baseUrl = cfg.WebhookUrl.Replace("/api/bot/update", "");
    await settings.InitDefaults(cfg.CardNumber, cfg.CardOwner, cfg.SupportUsername, baseUrl + "/admin");

    Console.WriteLine("✅ Database tayyor");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/admin", ctx => { ctx.Response.Redirect("/admin/index.html"); return Task.CompletedTask; });
app.MapControllers();

// ── Webhook ─────────────────────────────────────────────────────────────────
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(async () =>
{
    try
    {
        var bot = app.Services.GetRequiredService<ITelegramBotClient>();
        await bot.DeleteWebhook(dropPendingUpdates: true);

        if (!string.IsNullOrWhiteSpace(cfg.WebhookUrl) && !cfg.WebhookUrl.Contains("YOUR_NGROK"))
        {
            await bot.SetWebhook(cfg.WebhookUrl,
                allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
                dropPendingUpdates: true);
            Console.WriteLine($"✅ Webhook o'rnatildi: {cfg.WebhookUrl}");
        }
        else
            Console.WriteLine("⚠️ WebhookUrl sozlanmagan!");
    }
    catch (Exception ex) { Console.WriteLine($"❌ Webhook xatosi: {ex.Message}"); }
});

Console.WriteLine("🚀 Bot ishga tushdi!");
app.Run();