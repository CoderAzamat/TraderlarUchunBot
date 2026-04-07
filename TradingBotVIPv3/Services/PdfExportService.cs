using Microsoft.EntityFrameworkCore;
using System.Text;
using TradingBotVIPv3.Helpers;
using TradingBotVIPv3.Data;
using TradingBotVIPv3.Data.Models;

namespace TradingBotVIPv3.Services;

/// <summary>
/// Foydalanuvchilar ro'yxatini chiroyli HTML jadval sifatida PDF ga eksport qiladi.
/// Brauzerda PDF sifatida yuklanadi.
/// </summary>
public sealed class PdfExportService
{
    private readonly AppDbContext _db;

    public PdfExportService(AppDbContext db) => _db = db;

    public async Task<byte[]> GenerateUsersPdfAsync(CancellationToken ct = default)
    {
        var users = await _db.Users
            .Include(u => u.Subscriptions).ThenInclude(s => s.Plan)
            .Include(u => u.Payments)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(ct);

        var now = TimeHelper.NowTashkent();
        var total = users.Count;
        var activeVip = users.Count(u => u.Subscriptions.Any(s => s.IsActive && s.ExpireDate > DateTime.UtcNow));
        var totalIncome = users.SelectMany(u => u.Payments)
            .Where(p => p.Status == PaymentStatus.Approved)
            .Sum(p => p.Amount);

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html lang='uz'>
<head>
<meta charset='UTF-8'/>
<style>
  * {{ margin:0; padding:0; box-sizing:border-box; }}
  body {{ font-family:'Segoe UI',Arial,sans-serif; background:#fff; color:#1a1a2e; font-size:11px; }}

  .header {{ background:linear-gradient(135deg,#6366f1,#8b5cf6); color:#fff; padding:24px 32px; }}
  .header h1 {{ font-size:22px; font-weight:700; margin-bottom:4px; }}
  .header p  {{ font-size:12px; opacity:.85; }}

  .summary {{ display:flex; gap:0; border-bottom:2px solid #e2e8f0; }}
  .summary-card {{ flex:1; padding:16px 20px; text-align:center; border-right:1px solid #e2e8f0; }}
  .summary-card:last-child {{ border-right:none; }}
  .summary-card .val {{ font-size:22px; font-weight:700; color:#6366f1; }}
  .summary-card .lbl {{ font-size:11px; color:#64748b; margin-top:2px; }}

  .table-wrap {{ padding:20px 24px; }}
  .section-title {{ font-size:13px; font-weight:700; color:#374151; margin-bottom:12px;
                    padding-bottom:6px; border-bottom:2px solid #6366f1; display:inline-block; }}

  table {{ width:100%; border-collapse:collapse; }}
  thead tr {{ background:#6366f1; color:#fff; }}
  thead th {{ padding:9px 10px; text-align:left; font-size:10.5px; font-weight:600;
              letter-spacing:.04em; white-space:nowrap; }}
  tbody tr {{ border-bottom:1px solid #f1f5f9; }}
  tbody tr:nth-child(even) {{ background:#f8fafc; }}
  tbody tr:hover {{ background:#eef2ff; }}
  tbody td {{ padding:8px 10px; vertical-align:middle; }}

  .badge {{ display:inline-block; padding:2px 8px; border-radius:20px; font-size:10px; font-weight:600; }}
  .badge-vip    {{ background:#d1fae5; color:#065f46; }}
  .badge-novip  {{ background:#f1f5f9; color:#64748b; }}
  .badge-banned {{ background:#fee2e2; color:#991b1b; }}

  .footer {{ background:#f8fafc; border-top:1px solid #e2e8f0; padding:12px 24px;
             font-size:10px; color:#94a3b8; display:flex; justify-content:space-between; }}

  @media print {{
    body {{ -webkit-print-color-adjust:exact; print-color-adjust:exact; }}
  }}
</style>
</head>
<body>

<div class='header'>
  <h1>📊 Foydalanuvchilar Ro'yxati</h1>
  <p>Hisobot vaqti: {now:dd.MM.yyyy HH:mm:ss} (Toshkent vaqti)</p>
</div>

<div class='summary'>
  <div class='summary-card'><div class='val'>{total:N0}</div><div class='lbl'>Jami foydalanuvchi</div></div>
  <div class='summary-card'><div class='val'>{activeVip:N0}</div><div class='lbl'>Faol VIP</div></div>
  <div class='summary-card'><div class='val'>{totalIncome:N0}</div><div class='lbl'>Jami daromad (UZS)</div></div>
  <div class='summary-card'><div class='val'>{now:dd.MM.yyyy}</div><div class='lbl'>Hisobot sanasi</div></div>
</div>

<div class='table-wrap'>
  <div class='section-title'>Barcha Foydalanuvchilar</div>
  <table>
    <thead>
      <tr>
        <th>#</th>
        <th>ID</th>
        <th>Ism Familiya</th>
        <th>Telefon raqami</th>
        <th>Balans (UZS)</th>
        <th>VIP holati</th>
        <th>VIP tugash</th>
        <th>Ro'yxatdan o'tgan vaqt</th>
        <th>Holat</th>
      </tr>
    </thead>
    <tbody>
");

        int row = 1;
        foreach (var u in users)
        {
            var activeSub = u.Subscriptions
                .Where(s => s.IsActive && s.ExpireDate > DateTime.UtcNow)
                .OrderByDescending(s => s.ExpireDate)
                .FirstOrDefault();

            var vipBadge = u.IsBanned
                ? "<span class='badge badge-banned'>Bloklangan</span>"
                : activeSub is not null
                    ? $"<span class='badge badge-vip'>✓ {activeSub.Plan?.Title ?? "VIP"}</span>"
                    : "<span class='badge badge-novip'>Yo'q</span>";

            var vipExpire = activeSub is not null
                ? activeSub.ExpireDate.TashkentDate()
                : "—";

            sb.Append($@"
      <tr>
        <td>{row++}</td>
        <td><b>{u.Id}</b></td>
        <td>{HtmlEncode(u.FullName)}</td>
        <td>{HtmlEncode(u.PhoneNumber)}</td>
        <td>{u.Balance:N0}</td>
        <td>{vipBadge}</td>
        <td>{vipExpire}</td>
        <td>{u.CreatedAt.TashkentFull()}</td>
        <td>{(u.IsBanned ? "🚫" : "✅")}</td>
      </tr>");
        }

        sb.Append($@"
    </tbody>
  </table>
</div>

<div class='footer'>
  <span>VIP Bot Admin Panel</span>
  <span>Jami: {total} ta foydalanuvchi | Faol VIP: {activeVip} ta</span>
  <span>{now:dd.MM.yyyy HH:mm}</span>
</div>

</body>
</html>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<byte[]> GeneratePaymentsPdfAsync(CancellationToken ct = default)
    {
        var payments = await _db.Payments
            .Include(p => p.User)
            .Include(p => p.Plan)
            .OrderByDescending(p => p.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        var now = TimeHelper.NowTashkent();
        var approved = payments.Where(p => p.Status == PaymentStatus.Approved).Sum(p => p.Amount);

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html lang='uz'>
<head>
<meta charset='UTF-8'/>
<style>
  * {{ margin:0; padding:0; box-sizing:border-box; }}
  body {{ font-family:'Segoe UI',Arial,sans-serif; background:#fff; color:#1a1a2e; font-size:11px; }}
  .header {{ background:linear-gradient(135deg,#059669,#10b981); color:#fff; padding:24px 32px; }}
  .header h1 {{ font-size:22px; font-weight:700; margin-bottom:4px; }}
  .header p  {{ font-size:12px; opacity:.85; }}
  .summary {{ display:flex; border-bottom:2px solid #e2e8f0; }}
  .summary-card {{ flex:1; padding:16px 20px; text-align:center; border-right:1px solid #e2e8f0; }}
  .summary-card:last-child {{ border-right:none; }}
  .summary-card .val {{ font-size:20px; font-weight:700; color:#059669; }}
  .summary-card .lbl {{ font-size:11px; color:#64748b; margin-top:2px; }}
  .table-wrap {{ padding:20px 24px; }}
  table {{ width:100%; border-collapse:collapse; }}
  thead tr {{ background:#059669; color:#fff; }}
  thead th {{ padding:9px 10px; text-align:left; font-size:10.5px; font-weight:600; white-space:nowrap; }}
  tbody tr {{ border-bottom:1px solid #f1f5f9; }}
  tbody tr:nth-child(even) {{ background:#f0fdf4; }}
  tbody td {{ padding:7px 10px; vertical-align:middle; }}
  .s-approved {{ color:#065f46; background:#d1fae5; padding:2px 7px; border-radius:12px; font-size:10px; font-weight:600; }}
  .s-pending  {{ color:#92400e; background:#fef3c7; padding:2px 7px; border-radius:12px; font-size:10px; font-weight:600; }}
  .s-rejected {{ color:#991b1b; background:#fee2e2; padding:2px 7px; border-radius:12px; font-size:10px; font-weight:600; }}
  .footer {{ background:#f8fafc; border-top:1px solid #e2e8f0; padding:12px 24px; font-size:10px; color:#94a3b8; display:flex; justify-content:space-between; }}
</style>
</head>
<body>
<div class='header'>
  <h1>💳 To'lovlar Tarixi</h1>
  <p>Hisobot vaqti: {now:dd.MM.yyyy HH:mm:ss} (Toshkent vaqti)</p>
</div>
<div class='summary'>
  <div class='summary-card'><div class='val'>{payments.Count}</div><div class='lbl'>Jami to'lovlar</div></div>
  <div class='summary-card'><div class='val'>{payments.Count(p => p.Status == PaymentStatus.Approved)}</div><div class='lbl'>Tasdiqlangan</div></div>
  <div class='summary-card'><div class='val'>{payments.Count(p => p.Status == PaymentStatus.Pending)}</div><div class='lbl'>Kutayotgan</div></div>
  <div class='summary-card'><div class='val'>{approved:N0} UZS</div><div class='lbl'>Jami daromad</div></div>
</div>
<div class='table-wrap'>
<table>
  <thead>
    <tr>
      <th>#</th><th>ID</th><th>Foydalanuvchi</th><th>Telefon</th>
      <th>Reja</th><th>Summa (UZS)</th><th>Tur</th><th>Holat</th>
      <th>Vaqt (Toshkent)</th>
    </tr>
  </thead>
  <tbody>
");
        int row = 1;
        foreach (var p in payments)
        {
            var statusHtml = p.Status switch
            {
                PaymentStatus.Approved => "<span class='s-approved'>Tasdiqlangan</span>",
                PaymentStatus.Pending => "<span class='s-pending'>Kutmoqda</span>",
                _ => "<span class='s-rejected'>Rad etilgan</span>"
            };
            sb.Append($@"
    <tr>
      <td>{row++}</td>
      <td>{p.Id}</td>
      <td>{HtmlEncode(p.User.FullName)}</td>
      <td>{HtmlEncode(p.User.PhoneNumber)}</td>
      <td>{HtmlEncode(p.Plan?.Title ?? "Hisob to'ldirish")}</td>
      <td><b>{p.Amount:N0}</b></td>
      <td>{(p.Type == PaymentType.TopUp ? "TopUp" : "VIP")}</td>
      <td>{statusHtml}</td>
      <td>{p.CreatedAt.TashkentFull()}</td>
    </tr>");
        }

        sb.Append($@"
  </tbody>
</table>
</div>
<div class='footer'>
  <span>VIP Bot Admin Panel</span>
  <span>Jami: {payments.Count} ta to'lov | Tasdiqlangan: {approved:N0} UZS</span>
  <span>{now:dd.MM.yyyy HH:mm}</span>
</div>
</body></html>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string HtmlEncode(string? s) =>
        System.Net.WebUtility.HtmlEncode(s ?? "");
}