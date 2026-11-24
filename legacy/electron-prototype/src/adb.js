'use strict';

const { execFile } = require('child_process');
const { promisify } = require('util');

const execFileAsync = promisify(execFile);

const ADB = process.env.ADB_PATH || 'adb';
const TIMEOUT_MS = 8000;

async function run(args) {
  const { stdout } = await execFileAsync(ADB, args, {
    timeout: TIMEOUT_MS,
    windowsHide: true,
    maxBuffer: 1024 * 1024,
  });
  return stdout;
}

function clean(value) {
  const text = String(value || '').trim();
  return text === '' ? null : text;
}

function parseDevices(stdout) {
  return stdout
    .split(/\r?\n/)
    .slice(1)
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const parts = line.split(/\s+/);
      return {
        serial: parts[0],
        state: parts[1] || 'unknown',
        transport: parts.find((p) => p.startsWith('usb:')) || null,
      };
    });
}

async function getDeviceInfo() {
  const checkedAt = Date.now();

  let devices;
  try {
    devices = parseDevices(await run(['devices', '-l']));
  } catch (error) {
    return {
      adbAvailable: false,
      adbError: error.code === 'ENOENT'
        ? 'ADB executable not found. Install platform-tools or set ADB_PATH.'
        : `ADB failed: ${error.message}`,
      connected: false,
      checkedAt,
    };
  }

  const online = devices.find((d) => d.state === 'device');
  const unauthorized = devices.find((d) => d.state === 'unauthorized');
  const offline = devices.find((d) => d.state === 'offline');
  const device = online || unauthorized || offline || null;

  if (!device || device.state !== 'device') {
    return {
      adbAvailable: true,
      adbError: null,
      connected: false,
      state: device ? device.state : 'not-attached',
      deviceCount: devices.length,
      serial: device ? device.serial : null,
      checkedAt,
    };
  }

  const props = [
    'ro.product.marketname',
    'ro.product.model',
    'ro.mi.os.version.incremental',
    'ro.mi.os.version.name',
    'ro.build.version.release',
    'ro.build.version.incremental',
    'sys.usb.state',
    'ro.serialno',
  ];

  let values = [];
  try {
    const stdout = await run([
      'shell',
      props.map((prop) => `getprop ${prop}`).join('; '),
    ]);
    values = stdout.split(/\r?\n/);
  } catch (error) {
    return {
      adbAvailable: true,
      adbError: `Could not read device properties: ${error.message}`,
      connected: true,
      state: 'device',
      serial: device.serial,
      usbConnected: Boolean(device.transport),
      checkedAt,
    };
  }

  const [
    marketName,
    model,
    hyperOsIncremental,
    hyperOsName,
    androidVersion,
    buildIncremental,
    usbState,
    serialProp,
  ] = values.map(clean);

  const hyperOs = hyperOsIncremental || hyperOsName || buildIncremental || null;

  return {
    adbAvailable: true,
    adbError: null,
    connected: true,
    state: 'device',
    deviceCount: devices.length,
    model: model || null,
    marketName: marketName || null,
    hyperOs,
    androidVersion,
    serial: serialProp || device.serial,
    usbConnected: Boolean(device.transport) || Boolean(usbState && usbState.includes('adb')),
    usbState,
    checkedAt,
  };
}

module.exports = { getDeviceInfo };
