'use strict';

const api = window.assistant;
const $ = (id) => document.getElementById(id);

const BEIJING_OFFSET_MIN = 480; // UTC+8
const JAKARTA_OFFSET_MIN = 420; // UTC+7
const DAY_MS = 86400000;

const state = {
  offsetMs: 0,
  ntp: null,
  device: null,
  targetMode: localStorage.getItem('targetMode') || 'beijing',
  customTarget: Number(localStorage.getItem('customTarget')) || null,
  syncing: false,
  probing: false,
};

const timeFormatters = {
  beijing: new Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Shanghai', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }),
  jakarta: new Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Jakarta', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }),
  local: new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }),
};

const dateFormatters = {
  beijing: new Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Shanghai', weekday: 'short', day: '2-digit', month: 'short' }),
  jakarta: new Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Jakarta', weekday: 'short', day: '2-digit', month: 'short' }),
  local: new Intl.DateTimeFormat('en-GB', { weekday: 'short', day: '2-digit', month: 'short' }),
};

/* ---------- Helpers ---------- */

const pad = (value) => String(value).padStart(2, '0');

function setStatus(message, level = 'ok') {
  $('status-message').textContent = message;
  $('status-dot').dataset.state = level;
}

function setPill(id, textId, text, level) {
  $(id).dataset.state = level;
  $(textId).textContent = text;
}

function formatOffset(ms) {
  const rounded = Math.round(ms);
  const sign = rounded > 0 ? '+' : rounded < 0 ? '−' : '';
  return `${sign}${Math.abs(rounded)} ms`;
}

function timeOfDay(ms) {
  return new Date(ms).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' });
}

function nextLocalMidnight(nowMs, offsetMinutes) {
  const offset = offsetMinutes * 60000;
  return Math.floor((nowMs + offset) / DAY_MS) * DAY_MS + DAY_MS - offset;
}

function computeTarget(nowMs) {
  if (state.targetMode === 'jakarta') {
    return { at: nextLocalMidnight(nowMs, JAKARTA_OFFSET_MIN), label: 'Target · next 00:00 Jakarta time', zone: 'Asia/Jakarta', zoneName: 'Jakarta' };
  }
  if (state.targetMode === 'custom' && state.customTarget) {
    return { at: state.customTarget, label: 'Target · custom time', zone: null, zoneName: 'local' };
  }
  return { at: nextLocalMidnight(nowMs, BEIJING_OFFSET_MIN), label: 'Target · next 00:00 Beijing time (quota reset)', zone: 'Asia/Shanghai', zoneName: 'Beijing' };
}

/* ---------- Clock + countdown ---------- */

function renderClocks(nowMs) {
  const date = new Date(nowMs);
  for (const zone of ['beijing', 'jakarta', 'local']) {
    $(`time-${zone}`).textContent = timeFormatters[zone].format(date);
    $(`date-${zone}`).textContent = dateFormatters[zone].format(date);
  }
}

function renderCountdown(nowMs) {
  const target = computeTarget(nowMs);
  let remaining = target.at - nowMs;
  const done = remaining <= 0;
  if (done) remaining = 0;

  const days = Math.floor(remaining / DAY_MS);
  const hours = Math.floor((remaining % DAY_MS) / 3600000);
  const minutes = Math.floor((remaining % 3600000) / 60000);
  const seconds = Math.floor((remaining % 60000) / 1000);

  $('cd-days').textContent = pad(days);
  $('cd-hours').textContent = pad(hours);
  $('cd-minutes').textContent = pad(minutes);
  $('cd-seconds').textContent = pad(seconds);

  $('countdown').dataset.state = done ? 'done' : 'running';
  $('countdown-subtitle').textContent = state.targetMode === 'custom'
    ? (done ? 'Custom target reached' : 'Until the custom target')
    : (done ? 'Unlock window reached' : `Until the ${target.zoneName} unlock quota reset`);

  const targetText = target.zone
    ? new Intl.DateTimeFormat('en-GB', {
      timeZone: target.zone,
      weekday: 'short',
      day: '2-digit',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hourCycle: 'h23',
    }).format(new Date(target.at)) + ` ${target.zoneName}`
    : `Local ${timeOfDay(target.at)}`;

  $('target-label').textContent = target.label;
  $('target-abs').textContent = targetText;

  const progress = done ? 100 : Math.min(100, Math.max(0, (1 - remaining / DAY_MS) * 100));
  $('countdown-track').style.width = `${progress.toFixed(1)}%`;
}

function tick() {
  const now = Date.now() + state.offsetMs;
  renderClocks(now);
  renderCountdown(now);
}

/* ---------- Device ---------- */

function renderDevice(info) {
  const name = info && info.marketName ? info.marketName : 'POCO F7';
  $('device-name').textContent = name;

  const connected = Boolean(info && info.connected);
  const adbLevel = !info || !info.adbAvailable ? 'error' : connected ? 'ok' : info.state === 'unauthorized' ? 'warn' : 'error';

  $('device-status').dataset.state = adbLevel;
  $('device-status').textContent = !info || !info.adbAvailable
    ? 'ADB missing'
    : connected ? 'Connected' : info.state === 'unauthorized' ? 'Unauthorized' : 'Disconnected';

  $('device-sub').textContent = connected
    ? `Online via ADB${info.usbConnected ? ' · USB' : ''}`
    : !info || !info.adbAvailable ? 'ADB executable not found' : 'Waiting for device';

  $('spec-model').textContent = (info && info.model) || '—';
  $('spec-hyperos').textContent = (info && info.hyperOs) || '—';
  $('spec-android').textContent = info && info.androidVersion ? `Android ${info.androidVersion}` : '—';
  $('spec-serial').textContent = (info && info.serial) || '—';

  const adbCell = $('spec-adb');
  adbCell.textContent = !info || !info.adbAvailable ? 'Unavailable' : connected ? 'Online' : info.state === 'unauthorized' ? 'Unauthorized' : 'Offline';
  adbCell.className = adbLevel === 'ok' ? 'ok' : adbLevel === 'warn' ? 'warn' : 'error';

  const usbCell = $('spec-usb');
  if (!info || !info.adbAvailable) {
    usbCell.textContent = '—';
    usbCell.className = '';
  } else if (connected && info.usbConnected) {
    usbCell.textContent = 'USB attached';
    usbCell.className = 'ok';
  } else if (connected) {
    usbCell.textContent = 'ADB over network';
    usbCell.className = 'warn';
  } else {
    usbCell.textContent = 'No link';
    usbCell.className = 'error';
  }

  if (info && info.checkedAt) {
    $('device-foot').textContent = `Last probe ${timeOfDay(info.checkedAt)} · ${info.deviceCount ?? 0} device(s) seen by ADB`;
  } else {
    $('device-foot').textContent = 'No probe run yet.';
  }

  const pillLevel = !info || !info.adbAvailable ? 'error' : connected ? 'ok' : info.state === 'unauthorized' ? 'warn' : 'warn';
  setPill('dev-pill', 'dev-pill-text', !info || !info.adbAvailable ? 'ADB not found' : connected ? 'Device online' : 'ADB idle', pillLevel);
  $('adb-line').textContent = info && info.adbAvailable ? `ADB · ${connected ? 'online' : 'idle'}` : 'ADB · unavailable';
}

async function probeDevice({ quiet = false } = {}) {
  if (!api || state.probing) return;
  state.probing = true;
  if (!quiet) setStatus('Probing device over ADB…', 'busy');
  try {
    const info = await api.probeDevice();
    state.device = info;
    renderDevice(info);
    if (!quiet) {
      if (info.adbAvailable && info.connected) setStatus(`${info.marketName || info.model || 'Device'} connected over ADB.`, 'ok');
      else if (!info.adbAvailable) setStatus(info.adbError || 'ADB executable not found.', 'error');
      else if (info.state === 'unauthorized') setStatus('Device found but USB debugging is not authorized.', 'warn');
      else setStatus('No ADB device attached. Connect the phone with USB debugging enabled.', 'warn');
    }
  } catch (error) {
    setStatus(`Device probe failed: ${error.message}`, 'error');
  } finally {
    state.probing = false;
  }
}

/* ---------- NTP ---------- */

function renderNtp(result) {
  state.ntp = result;

  if (result.ok) {
    state.offsetMs = result.offsetMs;
    $('ntp-pill').dataset.state = 'ok';
    $('ntp-pill-text').textContent = 'NTP synced';
    $('clock-status').dataset.state = 'ok';
    $('clock-status').textContent = 'NTP corrected';
    $('ntp-title').textContent = `Synchronized · ${result.server}`;
    $('ntp-detail').textContent = `Stratum ${result.stratum} · round trip ${Math.round(result.roundTripMs)} ms · ${result.responders} responder(s) · at ${timeOfDay(result.checkedAt)}`;
    $('ntp-offset').textContent = formatOffset(result.offsetMs);
    $('time-foot').textContent = 'All three clocks are corrected by the measured NTP offset.';
    setStatus(`NTP synchronized with ${result.server} (${formatOffset(result.offsetMs)}).`, 'ok');
  } else {
    state.offsetMs = 0;
    $('ntp-pill').dataset.state = 'warn';
    $('ntp-pill-text').textContent = 'NTP unavailable';
    $('clock-status').dataset.state = 'warn';
    $('clock-status').textContent = 'System clock';
    $('ntp-title').textContent = 'NTP synchronization failed';
    $('ntp-detail').textContent = `${result.reason}. Falling back to the Windows system clock.`;
    $('ntp-offset').textContent = '—';
    $('time-foot').textContent = 'Showing the Windows system clock. Check UDP 123 on your network.';
    setStatus('NTP sync failed; clocks fall back to the system time.', 'warn');
  }
}

async function syncTime() {
  if (!api || state.syncing) return;
  state.syncing = true;
  $('sync-btn').disabled = true;
  setStatus('Querying NTP servers…', 'busy');
  try {
    renderNtp(await api.syncTime());
  } catch (error) {
    renderNtp({ ok: false, reason: error.message });
  } finally {
    state.syncing = false;
    $('sync-btn').disabled = false;
  }
}

/* ---------- Target controls ---------- */

function loadTargetControls() {
  const select = $('target-select');
  const custom = $('target-custom');
  select.value = state.targetMode;

  if (state.customTarget) {
    const date = new Date(state.customTarget - new Date().getTimezoneOffset() * 60000);
    custom.value = date.toISOString().slice(0, 19);
  }
  custom.hidden = state.targetMode !== 'custom';
}

function saveTarget() {
  localStorage.setItem('targetMode', state.targetMode);
  if (state.customTarget) localStorage.setItem('customTarget', String(state.customTarget));
  tick();
}

/* ---------- Wiring ---------- */

$('target-select').addEventListener('change', (event) => {
  state.targetMode = event.target.value;
  $('target-custom').hidden = state.targetMode !== 'custom';
  if (state.targetMode === 'custom' && !state.customTarget) {
    const soon = new Date(Date.now() + 5 * 60000);
    state.customTarget = soon.getTime();
    const local = new Date(soon.getTime() - soon.getTimezoneOffset() * 60000);
    $('target-custom').value = local.toISOString().slice(0, 19);
  }
  saveTarget();
});

$('target-custom').addEventListener('change', (event) => {
  const value = event.target.value;
  if (!value) return;
  state.customTarget = new Date(value).getTime();
  saveTarget();
});

$('refresh-btn').addEventListener('click', () => probeDevice());
$('sync-btn').addEventListener('click', () => syncTime());

document.querySelectorAll('[data-window]').forEach((button) => {
  button.addEventListener('click', () => {
    const action = button.dataset.window;
    if (api && api[action]) api[action]();
  });
});

document.querySelector('.titlebar').addEventListener('dblclick', () => {
  if (api) api.maximize();
});

document.addEventListener('keydown', (event) => {
  if (event.key === 'F5') {
    event.preventDefault();
    probeDevice();
  }
  if (event.key === 'F6') {
    event.preventDefault();
    syncTime();
  }
});

/* ---------- Boot ---------- */

$('local-zone-label').textContent = Intl.DateTimeFormat().resolvedOptions().timeZone || 'This machine';
loadTargetControls();
tick();
setInterval(tick, 250);

if (api) {
  probeDevice();
  syncTime();
  setInterval(() => probeDevice({ quiet: true }), 15000);
  setInterval(() => syncTime(), 15 * 60000);
} else {
  renderDevice(null);
  renderNtp({ ok: false, reason: 'Desktop bridge unavailable' });
}
