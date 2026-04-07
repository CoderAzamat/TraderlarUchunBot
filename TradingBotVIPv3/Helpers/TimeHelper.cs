namespace TradingBotVIPv3.Helpers;

/// <summary>
/// Barcha vaqtlar Toshkent (UTC+5) da ko'rsatiladi.
/// </summary>
public static class TimeHelper
{
    public static readonly TimeZoneInfo Tashkent =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "West Asia Standard Time" : "Asia/Tashkent");

    /// <summary>UTC → Toshkent</summary>
    public static DateTime ToTashkent(this DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tashkent);

    /// <summary>dd.MM.yyyy HH:mm:ss formatida</summary>
    public static string TashkentFull(this DateTime utc) =>
        utc.ToTashkent().ToString("dd.MM.yyyy HH:mm:ss");

    /// <summary>dd.MM.yyyy HH:mm formatida</summary>
    public static string TashkentShort(this DateTime utc) =>
        utc.ToTashkent().ToString("dd.MM.yyyy HH:mm");

    /// <summary>dd.MM.yyyy formatida</summary>
    public static string TashkentDate(this DateTime utc) =>
        utc.ToTashkent().ToString("dd.MM.yyyy");

    /// <summary>Hozirgi Toshkent vaqti</summary>
    public static DateTime NowTashkent() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tashkent);
}