using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;
using TradingBotVIPv3.Bot;

namespace TradingBotVIPv3.Controllers;

/// <summary>
/// Telegram shu endpointga POST yuboradi.
/// URL: /api/bot/update
/// </summary>
[ApiController]
[Route("api/bot")]
public sealed class BotController : ControllerBase
{
    private readonly UpdateHandler _handler;

    public BotController(UpdateHandler handler)
    {
        _handler = handler;
    }

    [HttpPost("update")]
    public async Task<IActionResult> Update(
        [FromBody] Update update,
        CancellationToken ct)
    {
        await _handler.HandleAsync(update, ct);
        return Ok();
    }

    // Sog'liqni tekshirish uchun (ixtiyoriy)
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", time = DateTime.UtcNow });
}