'use strict';

const dgram = require('dgram');

const SERVERS = [
  'time.google.com',
  'time.cloudflare.com',
  'pool.ntp.org',
];

const NTP_PORT = 123;
const NTP_DELTA = 2208988800; // seconds between 1900-01-01 and 1970-01-01
const QUERY_TIMEOUT_MS = 1800;

function toIso(ms) {
  return new Date(ms).toISOString();
}

function query(host) {
  return new Promise((resolve, reject) => {
    const socket = dgram.createSocket('udp4');
    const packet = Buffer.alloc(48);
    packet[0] = 0x23; // LI = 0, VN = 4, Mode = 3 (client)

    const t1 = Date.now() + Math.random(); // slightly desynchronised transmit stamp
    const seconds = Math.floor(t1 / 1000) + NTP_DELTA;
    const fraction = Math.floor(((t1 % 1000) / 1000) * 0x100000000);
    packet.writeUInt32BE(seconds >>> 0, 40);
    packet.writeUInt32BE(fraction >>> 0, 44);

    let settled = false;
    const finish = (error, result) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      try { socket.close(); } catch { /* already closed */ }
      if (error) reject(error);
      else resolve(result);
    };

    const timer = setTimeout(() => finish(new Error(`No reply from ${host}`)), QUERY_TIMEOUT_MS);

    socket.once('error', (error) => finish(error));
    socket.once('message', (message) => {
      const t4 = Date.now() + Math.random();
      if (message.length < 48) {
        finish(new Error(`Short reply from ${host}`));
        return;
      }
      const stratum = message[1];
      const secs = message.readUInt32BE(40);
      const frac = message.readUInt32BE(44);
      if (secs === 0) {
        finish(new Error(`Unusable timestamp from ${host}`));
        return;
      }
      const t3 = (secs - NTP_DELTA) * 1000 + (frac / 0x100000000) * 1000;
      const t2 = t3; // we do not read the receive stamp; RTT is derived below
      const offset = (t2 - t1 + (t3 - t4)) / 2;
      const rtt = (t4 - t1) - (t3 - t2);
      finish(null, {
        server: host,
        offsetMs: offset,
        roundTripMs: rtt,
        stratum,
        serverTime: t3,
        clientTime: t4,
      });
    });

    socket.send(packet, 0, packet.length, NTP_PORT, host, (error) => {
      if (error) finish(error);
    });
  });
}

async function syncTime() {
  const attempts = await Promise.allSettled(SERVERS.map((server) => query(server)));

  const replies = attempts
    .filter((attempt) => attempt.status === 'fulfilled')
    .map((attempt) => attempt.value)
    .filter((reply) => Math.abs(reply.offsetMs) < 120000)
    .sort((a, b) => a.roundTripMs - b.roundTripMs);

  if (replies.length === 0) {
    const errors = attempts
      .filter((attempt) => attempt.status === 'rejected')
      .map((attempt) => attempt.reason.message);
    return {
      ok: false,
      reason: errors[0] || 'No NTP server responded',
      checkedAt: Date.now(),
    };
  }

  const best = replies[0];
  return {
    ok: true,
    server: best.server,
    offsetMs: best.offsetMs,
    roundTripMs: best.roundTripMs,
    stratum: best.stratum,
    checkedAt: Date.now(),
    checkedAtIso: toIso(Date.now()),
    syncedTimeIso: toIso(Date.now() + best.offsetMs),
    responders: replies.length,
  };
}

module.exports = { syncTime };
