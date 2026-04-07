/* ═══════════════════════════════════════════
   VIP BOT ADMIN — JavaScript
═══════════════════════════════════════════ */
const API = '/api/admin';
let chart = null;

// ── Init ────────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    setupNav();
    checkStatus();
    loadDashboard();
    setInterval(checkStatus, 30_000);
    setInterval(updateClock, 1000);
    setInterval(refreshPendingBadge, 60_000);
});

// ── NAV ─────────────────────────────────────────────────────────────────────
function setupNav() {
    document.querySelectorAll('.nav-item').forEach(el => {
        el.addEventListener('click', () => {
            navigateTo(el.dataset.page);
            if (window.innerWidth <= 900) closeSidebar();
        });
    });
}

function navigateTo(page) {
    document.querySelectorAll('.nav-item').forEach(el => el.classList.toggle('active', el.dataset.page === page));
    document.querySelectorAll('.page').forEach(el => el.classList.remove('active'));
    const pg = document.getElementById('page-' + page);
    if (pg) pg.classList.add('active');
    const titles = {
        dashboard: 'Dashboard', users: 'Foydalanuvchilar', payments: "To'lovlar",
        subscriptions: 'Obunalar', channels: 'VIP Kanallar', plans: 'Rejalar',
        broadcast: 'Broadcast', settings: 'Sozlamalar'
    };
    document.getElementById('page-title').textContent = titles[page] || page;
    const loaders = {
        dashboard: loadDashboard, users: () => loadUsers(1),
        payments: () => loadPayments(1), subscriptions: () => loadSubs(1),
        channels: loadChannels, plans: loadPlans, settings: loadSettings
    };
    loaders[page]?.();
}

function toggleSidebar() {
    const sb = document.getElementById('sidebar');
    const ov = document.getElementById('overlay');
    sb.classList.toggle('open');
    ov.style.display = sb.classList.contains('open') ? 'block' : 'none';
}
function closeSidebar() {
    document.getElementById('sidebar').classList.remove('open');
    document.getElementById('overlay').style.display = 'none';
}

function refreshPage() {
    const icon = document.getElementById('refresh-icon');
    icon.style.animation = 'spin .5s linear';
    setTimeout(() => icon.style.animation = '', 600);
    const active = document.querySelector('.nav-item.active');
    if (active) navigateTo(active.dataset.page);
}

// Clock
function updateClock() {
    const el = document.getElementById('server-time');
    if (el) el.textContent = new Date().toLocaleString('uz-UZ', { timeZone: 'Asia/Tashkent', hour12: false });
}

// Status
async function checkStatus() {
    const led = document.getElementById('status-led');
    const lbl = document.getElementById('status-label');
    try {
        const r = await fetch('/api/bot/health');
        if (r.ok) { led.className = 'status-led on'; lbl.textContent = 'Bot ishlayapti'; }
        else throw 0;
    } catch {
        led.className = 'status-led off'; lbl.textContent = 'Bot offline';
    }
}

async function refreshPendingBadge() {
    try {
        const d = await get(`${API}/dashboard`);
        updateBadge(d.pendingPayments);
    } catch { }
}
function updateBadge(n) {
    const b = document.getElementById('pending-badge');
    b.textContent = n; b.style.display = n > 0 ? 'flex' : 'none';
}

// ═══════════════════════════════════════════════════════════════════════════
//  DASHBOARD
// ═══════════════════════════════════════════════════════════════════════════
async function loadDashboard() {
    try {
        const d = await get(`${API}/dashboard`);
        document.getElementById('kpi-totalUsers').textContent = fmt(d.totalUsers);
        document.getElementById('kpi-todayUsers').textContent = fmt(d.todayUsers);
        document.getElementById('kpi-activeVip').textContent = fmt(d.activeVip);
        document.getElementById('kpi-pending').textContent = fmt(d.pendingPayments);
        document.getElementById('kpi-todayIncome').textContent = fmtM(d.todayIncome);
        document.getElementById('kpi-monthIncome').textContent = fmtM(d.monthIncome);
        document.getElementById('kpi-totalIncome').textContent = fmtM(d.totalIncome);
        updateBadge(d.pendingPayments);
        drawChart(d.chartData);
        renderRecentPayments(d.recentPayments);
    } catch (e) { toast('Dashboard yuklashda xato', 'error'); }
}

function drawChart(data) {
    const ctx = document.getElementById('chart-income')?.getContext('2d');
    if (!ctx) return;
    if (chart) chart.destroy();
    chart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: data.map(d => d.date),
            datasets: [{
                label: 'Daromad',
                data: data.map(d => d.amount),
                borderColor: '#6366f1',
                backgroundColor: 'rgba(99,102,241,.08)',
                borderWidth: 2.5,
                pointBackgroundColor: '#6366f1',
                pointRadius: 4,
                pointHoverRadius: 6,
                fill: true,
                tension: 0.4
            }]
        },
        options: {
            responsive: true, maintainAspectRatio: false,
            plugins: { legend: { display: false }, tooltip: { callbacks: { label: c => fmtM(c.raw) } } },
            scales: {
                x: { grid: { color: 'rgba(255,255,255,.05)' }, ticks: { color: '#64748b', font: { size: 11 } } },
                y: { grid: { color: 'rgba(255,255,255,.05)' }, ticks: { color: '#64748b', font: { size: 11 }, callback: v => fmtShort(v) } }
            }
        }
    });
}

function renderRecentPayments(payments) {
    const el = document.getElementById('recent-payments');
    if (!el) return;
    el.innerHTML = payments.map(p => `
    <div class="mini-row">
      <div class="mini-row-left">
        <span class="mini-row-name">${esc(p.userName)}</span>
        <span class="mini-row-sub">${esc(p.userPhone)} · ${esc(p.planTitle)}</span>
      </div>
      <div class="mini-row-right">
        <div class="mini-row-amount">${fmtM(p.amount)}</div>
        <div class="mini-row-time">${esc(p.createdAt)}</div>
      </div>
    </div>`).join('');
}

// ═══════════════════════════════════════════════════════════════════════════
//  FOYDALANUVCHILAR
// ═══════════════════════════════════════════════════════════════════════════
let uPage = 1; const U_LIMIT = 20;

async function loadUsers(page = 1) {
    uPage = page;
    const search = document.getElementById('user-search')?.value || '';
    try {
        const d = await get(`${API}/users?page=${page}&limit=${U_LIMIT}&search=${enc(search)}`);
        document.getElementById('users-info').textContent = `Jami: ${fmt(d.total)} ta`;
        document.getElementById('users-tbody').innerHTML = d.users.map(u => `
      <tr>
        <td><code>${u.id}</code></td>
        <td>
          <div style="font-weight:600">${esc(u.fullName)}</div>
          <div style="font-size:11px;color:var(--muted)">#${u.telegramId}</div>
        </td>
        <td><code>${esc(u.phoneNumber)}</code></td>
        <td style="font-weight:600">${fmtM(u.balance)}</td>
        <td>${u.hasActiveSub ? '<span class="tag tag-purple">⭐ VIP</span>' : '<span class="tag tag-gray">—</span>'}</td>
        <td style="font-size:12px;color:var(--muted)">${esc(u.createdAt)}</td>
        <td>${u.isBanned ? '<span class="tag tag-red">🚫 Bloklangan</span>' : '<span class="tag tag-green">✅ Faol</span>'}</td>
        <td>
          <button class="btn btn-sm btn-ghost btn-icon" onclick="viewUser(${u.id})" title="Ko'rish"><i class="fa-solid fa-eye"></i></button>
        </td>
      </tr>`).join('');
        renderPg('users-pg', d.total, U_LIMIT, page, loadUsers);
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

let _debounceTimer;
function debounce(fn, ms) { return () => { clearTimeout(_debounceTimer); _debounceTimer = setTimeout(fn, ms); }; }

async function viewUser(id) {
    try {
        const u = await get(`${API}/users/${id}`);
        document.getElementById('modal-user-title').textContent = u.fullName;
        const sub = u.subscriptions.find(s => s.isActive);
        document.getElementById('modal-user-body').innerHTML = `
      <div class="user-detail-grid">
        <div class="user-detail-item"><span class="detail-lbl">ID</span><span class="detail-val"><code>${u.id}</code></span></div>
        <div class="user-detail-item"><span class="detail-lbl">Telegram ID</span><span class="detail-val"><code>${u.telegramId}</code></span></div>
        <div class="user-detail-item"><span class="detail-lbl">Ism</span><span class="detail-val">${esc(u.fullName)}</span></div>
        <div class="user-detail-item"><span class="detail-lbl">Telefon</span><span class="detail-val"><code>${esc(u.phoneNumber)}</code></span></div>
        <div class="user-detail-item"><span class="detail-lbl">Balans</span><span class="detail-val">${fmtM(u.balance)}</span></div>
        <div class="user-detail-item"><span class="detail-lbl">VIP</span><span class="detail-val">${sub ? `⭐ ${sub.planTitle} (${sub.expireDate})` : '—'}</span></div>
        <div class="user-detail-item"><span class="detail-lbl">Holat</span><span class="detail-val">${u.isBanned ? '🚫 Bloklangan' : '✅ Faol'}</span></div>
        <div class="user-detail-item"><span class="detail-lbl">Qo'shilgan</span><span class="detail-val">${esc(u.createdAt)}</span></div>
      </div>

      <div class="user-actions-block">
        <div class="user-action-row">
          <input type="number" id="bal-amt-${u.id}" placeholder="Summa (+ qo'shish, - ayirish)" />
          <input type="text"   id="bal-note-${u.id}" placeholder="Sabab" style="width:160px" />
          <button class="btn btn-success btn-sm" onclick="addBal(${u.id})">💰 Balans</button>
        </div>
        <div class="user-action-row">
          <input type="text" id="msg-${u.id}" placeholder="Foydalanuvchiga xabar..." style="flex:1"/>
          <button class="btn btn-ghost btn-sm" onclick="sendMsg(${u.id})">✉️ Yuborish</button>
        </div>
        <div class="user-action-row">
          ${u.isBanned
                ? `<button class="btn btn-success btn-sm" onclick="banUser(${u.id}, false)">🔓 Blokdan chiqar</button>`
                : `<button class="btn btn-danger btn-sm"  onclick="banUser(${u.id}, true)">🚫 Bloklash</button>`}
        </div>
      </div>

      ${u.payments.length ? `
        <div class="user-payments-block">
          <h4>Oxirgi to'lovlar</h4>
          ${u.payments.map(p => `
            <div class="mini-payment">
              <span>${esc(p.planTitle)}</span>
              <span>${fmtM(p.amount)}</span>
              <span>${statusTag(p.status)}</span>
              <span style="font-size:11px;color:var(--muted)">${esc(p.createdAt)}</span>
            </div>`).join('')}
        </div>` : ''}`;
        openModal('modal-user');
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

async function addBal(id) {
    const amt = parseFloat(document.getElementById(`bal-amt-${id}`).value);
    const note = document.getElementById(`bal-note-${id}`).value;
    if (!amt || isNaN(amt)) { toast("Miqdorni kiriting", 'error'); return; }
    try {
        const r = await post(`${API}/users/${id}/balance`, { amount: amt, note });
        toast(`Balans: ${fmtM(r.balance)}`, 'success');
        closeModal('modal-user'); loadUsers(uPage);
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

async function sendMsg(id) {
    const text = document.getElementById(`msg-${id}`).value.trim();
    if (!text) { toast("Xabar kiriting", 'error'); return; }
    try {
        await post(`${API}/users/${id}/message`, { text });
        toast("Xabar yuborildi ✓", 'success'); closeModal('modal-user');
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

async function banUser(id, ban) {
    if (!confirm(ban ? 'Bloklaysizmi?' : 'Blokdan chiqarasizmi?')) return;
    try {
        await post(`${API}/users/${id}/ban`, { ban });
        toast(ban ? '🚫 Bloklandi' : '✅ Blok olib tashlandi', 'success');
        closeModal('modal-user'); loadUsers(uPage);
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

async function exportUsers() {
    window.open(`${API}/export/users`, '_blank');
}

// ═══════════════════════════════════════════════════════════════════════════
//  TO'LOVLAR
// ═══════════════════════════════════════════════════════════════════════════
let pPage = 1, pStatus = null; const P_LIMIT = 20;

function setPayFilter(s, btn) {
    pStatus = s;
    document.querySelectorAll('#page-payments .pill-btn').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    loadPayments(1);
}

async function loadPayments(page = 1) {
    pPage = page;
    const url = `${API}/payments?page=${page}&limit=${P_LIMIT}${pStatus !== null ? '&status=' + pStatus : ''}`;
    try {
        const d = await get(url);
        document.getElementById('payments-info').textContent = `Jami: ${fmt(d.total)} ta`;
        document.getElementById('payments-tbody').innerHTML = d.payments.map(p => `
      <tr>
        <td><code>#${p.id}</code></td>
        <td>
          <div style="font-weight:600">${esc(p.userName)}</div>
        </td>
        <td><code style="font-size:12px">${esc(p.userPhone)}</code></td>
        <td>${esc(p.planTitle)}</td>
        <td style="font-weight:700">${fmtM(p.amount)}</td>
        <td>${p.type === 0 ? '<span class="tag tag-blue">TopUp</span>' : '<span class="tag tag-purple">VIP</span>'}</td>
        <td>${statusTag(p.status)}</td>
        <td style="font-size:12px;color:var(--muted);white-space:nowrap">${esc(p.createdAt)}</td>
        <td style="white-space:nowrap">
          ${p.receiptFileId ? `<button class="btn btn-sm btn-ghost btn-icon" title="Chek" onclick="viewReceipt('${esc(p.receiptFileId)}')"><i class="fa-solid fa-image"></i></button>` : ''}
          ${p.status === 0 ? `
            <button class="btn btn-sm btn-success btn-icon" title="Tasdiqlash" onclick="approvePay(${p.id})"><i class="fa-solid fa-check"></i></button>
            <button class="btn btn-sm btn-danger  btn-icon" title="Rad etish"  onclick="rejectPay(${p.id})"><i class="fa-solid fa-xmark"></i></button>` : ''}
        </td>
      </tr>`).join('');
        renderPg('payments-pg', d.total, P_LIMIT, page, loadPayments);
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

async function approvePay(id) {
    if (!confirm("Tasdiqlaysizmi?")) return;
    try {
        await post(`${API}/payments/${id}/approve`);
        toast("✅ Tasdiqlandi", 'success'); loadPayments(pPage); loadDashboard();
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

async function rejectPay(id) {
    const reason = prompt("Rad etish sababi (bo'sh qoldirsa ham bo'ladi):") ?? '';
    try {
        await post(`${API}/payments/${id}/reject`, { reason });
        toast("❌ Rad etildi", 'info'); loadPayments(pPage);
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

function viewReceipt(fileId) {
    document.getElementById('modal-receipt-body').innerHTML = `
    <p style="color:var(--muted);font-size:13px;margin-bottom:12px">Chekni Telegram botda ko'rish uchun file ID:</p>
    <code style="display:block;background:rgba(255,255,255,.07);padding:12px;border-radius:8px;word-break:break-all;font-size:12px">${esc(fileId)}</code>`;
    openModal('modal-receipt');
}

async function exportPayments() { window.open(`${API}/export/payments`, '_blank'); }

// ═══════════════════════════════════════════════════════════════════════════
//  OBUNALAR
// ═══════════════════════════════════════════════════════════════════════════
let sPage = 1, sActive = null; const S_LIMIT = 20;

function setSubFilter(a, btn) {
    sActive = a;
    document.querySelectorAll('#page-subscriptions .pill-btn').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    loadSubs(1);
}

async function loadSubs(page = 1) {
    sPage = page;
    const url = `${API}/subscriptions?page=${page}&limit=${S_LIMIT}${sActive !== null ? '&active=' + sActive : ''}`;
    try {
        const d = await get(url);
        document.getElementById('subs-info').textContent = `Jami: ${fmt(d.total)} ta`;
        document.getElementById('subs-tbody').innerHTML = d.subs.map(s => `
      <tr>
        <td><code>${s.id}</code></td>
        <td style="font-weight:600">${esc(s.userName)}</td>
        <td><code style="font-size:12px">${esc(s.userPhone)}</code></td>
        <td>${esc(s.planTitle)}</td>
        <td style="font-size:12px">${esc(s.startDate)}</td>
        <td style="font-size:12px;white-space:nowrap">${esc(s.expireDate)}</td>
        <td>${s.isActive ? (s.daysLeft > 0 ? `<span class="tag tag-green">${s.daysLeft} kun</span>` : '<span class="tag tag-amber">Bugun</span>') : '<span class="tag tag-gray">—</span>'}</td>
        <td>${s.isActive ? '<span class="tag tag-green">✅ Faol</span>' : '<span class="tag tag-gray">Tugagan</span>'}</td>
      </tr>`).join('');
        renderPg('subs-pg', d.total, S_LIMIT, page, loadSubs);
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

// ═══════════════════════════════════════════════════════════════════════════
//  VIP KANALLAR
// ═══════════════════════════════════════════════════════════════════════════
async function loadChannels() {
    try {
        const channels = await get(`${API}/channels`);
        document.getElementById('channels-grid').innerHTML = channels.length === 0
            ? '<div style="padding:40px;text-align:center;color:var(--muted)">Hozircha kanal yo\'q</div>'
            : channels.map(c => `
        <div class="channel-card">
          <div class="channel-card-head">
            <div>
              <div class="channel-name">${esc(c.title)}</div>
              <div class="channel-id">${c.telegramChannelId}</div>
            </div>
            <span class="tag ${c.isActive ? 'tag-green' : 'tag-gray'}">${c.isActive ? 'Faol' : 'Nofaol'}</span>
          </div>
          <div class="channel-stats">
            <div class="channel-stat"><b>${c.plansCount}</b>Faol rejalar</div>
            <div class="channel-stat"><b>${esc(c.createdAt)}</b>Qo'shilgan</div>
          </div>
          <div class="channel-actions">
            <button class="btn btn-sm btn-ghost" onclick="editChannel(${JSON.stringify(c).replace(/"/g, '&quot;')})">
              <i class="fa-solid fa-pen"></i> Tahrirlash
            </button>
            ${c.isActive
                    ? `<button class="btn btn-sm btn-danger" onclick="toggleChannel(${c.id}, false)"><i class="fa-solid fa-ban"></i></button>`
                    : `<button class="btn btn-sm btn-success" onclick="toggleChannel(${c.id}, true)"><i class="fa-solid fa-circle-check"></i></button>`}
            <button class="btn btn-sm btn-danger btn-icon" onclick="deleteChannel(${c.id})"><i class="fa-solid fa-trash"></i></button>
          </div>
        </div>`).join('');
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

function openChannelModal(id = null) {
    document.getElementById('ch-id').value = id ?? '';
    document.getElementById('ch-name').value = '';
    document.getElementById('ch-tgid').value = '';
    document.getElementById('ch-active').checked = true;
    document.getElementById('modal-channel-title').textContent = id ? 'Kanalni tahrirlash' : 'Yangi kanal';
    openModal('modal-channel');
}

function editChannel(c) {
    document.getElementById('ch-id').value = c.id;
    document.getElementById('ch-name').value = c.title;
    document.getElementById('ch-tgid').value = c.telegramChannelId;
    document.getElementById('ch-active').checked = c.isActive;
    document.getElementById('modal-channel-title').textContent = 'Kanalni tahrirlash';
    openModal('modal-channel');
}

async function saveChannel() {
    const id = document.getElementById('ch-id').value;
    const title = document.getElementById('ch-name').value.trim();
    const tgId = document.getElementById('ch-tgid').value.trim();
    const active = document.getElementById('ch-active').checked;
    if (!title) { toast("Kanal nomini kiriting", 'error'); return; }
    if (!tgId) { toast("Telegram ID ni kiriting", 'error'); return; }
    try {
        const body = { title, telegramChannelId: Number(tgId), isActive: active };
        if (id) await put(`${API}/channels/${id}`, body);
        else await post(`${API}/channels`, body);
        toast('Saqlandi ✓', 'success'); closeModal('modal-channel'); loadChannels();
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

async function toggleChannel(id, active) {
    const ch = (await get(`${API}/channels`)).find(c => c.id === id);
    if (!ch) return;
    try { await put(`${API}/channels/${id}`, { ...ch, isActive: active }); loadChannels(); }
    catch (e) { toast('Xato: ' + e.message, 'error'); }
}

async function deleteChannel(id) {
    if (!confirm("Kanalni o'chirasizmi?")) return;
    try { await del(`${API}/channels/${id}`); toast("O'chirildi", 'info'); loadChannels(); }
    catch (e) { toast('Xato: ' + e.message, 'error'); }
}

// ═══════════════════════════════════════════════════════════════════════════
//  REJALAR
// ═══════════════════════════════════════════════════════════════════════════
async function loadPlans() {
    try {
        const plans = await get(`${API}/plans`);
        document.getElementById('plans-grid').innerHTML = plans.length === 0
            ? '<div style="padding:40px;text-align:center;color:var(--muted)">Hozircha reja yo\'q</div>'
            : plans.map(p => `
        <div class="plan-card">
          <div class="plan-card-head">
            <div class="plan-icon"><i class="fa-solid fa-star"></i></div>
            <div>
              <div class="plan-name">${esc(p.title)}</div>
              <div class="plan-channel">${esc(p.channelTitle)}</div>
            </div>
            <span class="tag ${p.isActive ? 'tag-green' : 'tag-gray'}" style="margin-left:auto">${p.isActive ? 'Faol' : 'Nofaol'}</span>
          </div>
          <div class="plan-details">
            <div class="plan-detail-item"><div class="plan-detail-label">Narx</div><div class="plan-detail-value">${fmtM(p.price)}</div></div>
            <div class="plan-detail-item"><div class="plan-detail-label">Muddat</div><div class="plan-detail-value">${p.durationDays} kun</div></div>
            <div class="plan-detail-item"><div class="plan-detail-label">Sotilgan</div><div class="plan-detail-value">${p.soldCount} ta</div></div>
            <div class="plan-detail-item"><div class="plan-detail-label">Qo'shilgan</div><div class="plan-detail-value">${esc(p.createdAt)}</div></div>
          </div>
          <div class="plan-actions">
            <button class="btn btn-sm btn-ghost" onclick="editPlan(${JSON.stringify(p).replace(/"/g, '&quot;')})"><i class="fa-solid fa-pen"></i> Tahrirlash</button>
            ${p.isActive
                    ? `<button class="btn btn-sm btn-danger btn-icon" onclick="deletePlan(${p.id})" title="Nofaol qilish"><i class="fa-solid fa-ban"></i></button>`
                    : `<button class="btn btn-sm btn-success btn-icon" onclick="activatePlan(${p.id})" title="Faollashtirish"><i class="fa-solid fa-check"></i></button>`}
          </div>
        </div>`).join('');
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

async function openPlanModal() {
    await fillChannels();
    document.getElementById('pl-id').value = '';
    document.getElementById('pl-name').value = '';
    document.getElementById('pl-days').value = '';
    document.getElementById('pl-price').value = '';
    document.getElementById('pl-active').checked = true;
    document.getElementById('modal-plan-title').textContent = 'Yangi reja';
    openModal('modal-plan');
}

async function editPlan(p) {
    await fillChannels();
    document.getElementById('pl-id').value = p.id;
    document.getElementById('pl-name').value = p.title;
    document.getElementById('pl-days').value = p.durationDays;
    document.getElementById('pl-price').value = p.price;
    document.getElementById('pl-channel').value = p.vipChannelId;
    document.getElementById('pl-active').checked = p.isActive;
    document.getElementById('modal-plan-title').textContent = 'Rejani tahrirlash';
    openModal('modal-plan');
}

async function fillChannels() {
    const sel = document.getElementById('pl-channel');
    const chs = await get(`${API}/channels`);
    sel.innerHTML = chs.map(c => `<option value="${c.id}">${esc(c.title)}</option>`).join('');
}

async function savePlan() {
    const id = document.getElementById('pl-id').value;
    const title = document.getElementById('pl-name').value.trim();
    const days = parseInt(document.getElementById('pl-days').value);
    const price = parseFloat(document.getElementById('pl-price').value);
    const chId = parseInt(document.getElementById('pl-channel').value);
    const active = document.getElementById('pl-active').checked;
    if (!title) { toast("Nom kiriting", 'error'); return; }
    if (!days || days < 1) { toast("Kunlar kiriting", 'error'); return; }
    if (isNaN(price)) { toast("Narx kiriting", 'error'); return; }
    if (!chId) { toast("Kanal tanlang", 'error'); return; }
    try {
        const body = { title, durationDays: days, price, vipChannelId: chId, isActive: active };
        if (id) await put(`${API}/plans/${id}`, body);
        else await post(`${API}/plans`, body);
        toast('Saqlandi ✓', 'success'); closeModal('modal-plan'); loadPlans();
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

async function deletePlan(id) {
    if (!confirm("Rejani nofaol qilasizmi?")) return;
    try { await del(`${API}/plans/${id}`); toast("Nofaol qilindi", 'info'); loadPlans(); }
    catch (e) { toast('Xato: ' + e.message, 'error'); }
}

async function activatePlan(id) {
    const plans = await get(`${API}/plans`);
    const p = plans.find(x => x.id === id);
    if (!p) return;
    try { await put(`${API}/plans/${id}`, { ...p, isActive: true }); loadPlans(); }
    catch (e) { toast('Xato: ' + e.message, 'error'); }
}

// ═══════════════════════════════════════════════════════════════════════════
//  BROADCAST
// ═══════════════════════════════════════════════════════════════════════════
function previewBc() {
    const text = document.getElementById('bc-text').value;
    const pr = document.getElementById('bc-preview');
    pr.innerHTML = '<b>Ko\'rinishi:</b><br/><br/>' + text;
    pr.style.display = 'block';
}

async function sendBc() {
    const text = document.getElementById('bc-text').value.trim();
    if (!text) { toast("Xabar kiriting", 'error'); return; }
    if (!confirm("Barcha foydalanuvchilarga yuborasizmi?")) return;
    const btn = document.getElementById('bc-btn');
    btn.disabled = true; btn.textContent = 'Yuborilmoqda...';
    const res = document.getElementById('bc-result');
    try {
        const r = await post(`${API}/broadcast`, { text });
        res.className = 'bc-ok';
        res.innerHTML = `✅ Yuborildi: <b>${r.sent}</b> · Xato: <b>${r.failed}</b> · Jami: <b>${r.total}</b>`;
        res.style.display = 'block';
    } catch (e) {
        res.className = 'bc-err'; res.textContent = 'Xato: ' + e.message; res.style.display = 'block';
    } finally {
        btn.disabled = false; btn.innerHTML = '<i class="fa-solid fa-paper-plane"></i> Yuborish';
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  SOZLAMALAR
// ═══════════════════════════════════════════════════════════════════════════
async function loadSettings() {
    try {
        const s = await get(`${API}/settings`);
        document.getElementById('settings-body').innerHTML = `
      <div class="setting-row"><span class="setting-lbl">Karta raqami</span><span class="setting-val"><code>${esc(s.cardNumber)}</code></span></div>
      <div class="setting-row"><span class="setting-lbl">Karta egasi</span><span class="setting-val">${esc(s.cardOwner)}</span></div>
      <div class="setting-row"><span class="setting-lbl">Support</span><span class="setting-val">${esc(s.supportUsername)}</span></div>
      <div class="setting-row"><span class="setting-lbl">Eslatma vaqti</span><span class="setting-val">${s.notifyHoursBefore} soat oldin</span></div>
      <div class="setting-row"><span class="setting-lbl">SMS</span><span class="setting-val">${s.smsEnabled ? '<span class="tag tag-green">✅ Yoqilgan</span>' : '<span class="tag tag-gray">O\'chirilgan</span>'}</span></div>
      <div class="setting-row"><span class="setting-lbl">Server vaqti</span><span class="setting-val">${esc(s.serverTime)}</span></div>
      <div class="setting-row"><span class="setting-lbl">Webhook URL</span><span class="setting-val" style="font-size:11px"><code>${esc(s.webhookUrl)}</code></span></div>
    `;
    } catch (e) { toast('Xato: ' + e.message, 'error'); }
}

// ═══════════════════════════════════════════════════════════════════════════
//  HELPERS
// ═══════════════════════════════════════════════════════════════════════════
async function get(url) {
    const r = await fetch(url);
    if (!r.ok) { let e; try { e = (await r.json()).error; } catch { e = r.statusText; } throw new Error(e); }
    return r.json();
}
async function post(url, body = null) {
    const r = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : null });
    if (!r.ok) { let e; try { e = (await r.json()).error; } catch { e = r.statusText; } throw new Error(e); }
    return r.status === 204 ? null : r.json();
}
async function put(url, body) {
    const r = await fetch(url, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (!r.ok) { let e; try { e = (await r.json()).error; } catch { e = r.statusText; } throw new Error(e); }
    return r.json();
}
async function del(url) {
    const r = await fetch(url, { method: 'DELETE' });
    if (!r.ok) { let e; try { e = (await r.json()).error; } catch { e = r.statusText; } throw new Error(e); }
    return r.status === 204 ? null : r.json();
}

function openModal(id) { document.getElementById(id).classList.add('open'); }
function closeModal(id) { document.getElementById(id).classList.remove('open'); }
document.querySelectorAll('.modal-bg').forEach(m => m.addEventListener('click', e => { if (e.target === m) m.classList.remove('open'); }));

function renderPg(id, total, limit, current, fn) {
    const pages = Math.ceil(total / limit);
    const el = document.getElementById(id);
    if (!el || pages <= 1) { if (el) el.innerHTML = ''; return; }
    let h = '';
    if (current > 1) h += `<button class="pg-btn" onclick="${fn.name}(${current - 1})">‹</button>`;
    const s = Math.max(1, current - 2), e = Math.min(pages, current + 2);
    for (let i = s; i <= e; i++) h += `<button class="pg-btn ${i === current ? 'active' : ''}" onclick="${fn.name}(${i})">${i}</button>`;
    if (current < pages) h += `<button class="pg-btn" onclick="${fn.name}(${current + 1})">›</button>`;
    el.innerHTML = h;
}

function statusTag(s) {
    const m = ['<span class="tag tag-amber">⏳ Kutmoqda</span>', '<span class="tag tag-green">✅ Tasdiqlangan</span>', '<span class="tag tag-red">❌ Rad etilgan</span>'];
    return m[s] ?? '<span class="tag tag-gray">—</span>';
}

function toast(msg, type = 'info') {
    const icons = { success: 'fa-circle-check', error: 'fa-circle-exclamation', info: 'fa-circle-info' };
    const t = document.createElement('div');
    t.className = `toast toast-${type}`;
    t.innerHTML = `<i class="fa-solid ${icons[type] || icons.info} toast-icon"></i><span>${esc(msg)}</span>`;
    document.getElementById('toasts').appendChild(t);
    setTimeout(() => t.style.opacity = '0', 3600);
    setTimeout(() => t.remove(), 4000);
}

function fmt(n) { return Number(n).toLocaleString(); }
function fmtM(n) { return Number(n).toLocaleString('uz-UZ') + ' UZS'; }
function fmtShort(n) { if (n >= 1e6) return (n / 1e6).toFixed(1) + 'M'; if (n >= 1e3) return (n / 1e3).toFixed(0) + 'K'; return n; }
function enc(s) { return encodeURIComponent(s || ''); }
function esc(s) { return (s ?? '').toString().replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }