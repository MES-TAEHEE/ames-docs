const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname, '../../../06_Web/AMES.Web/wwwroot/js/zebra-print.js'), 'utf8');

function printerPort({ failWrite = false, gate = null } = {}) {
    const state = { opens: 0, closes: 0, chunks: [], attempts: 0 };
    const writer = { async write(bytes) {
        state.attempts++;
        if (gate) await gate;
        if (failWrite) throw new Error('Disconnected');
        state.chunks.push(Buffer.from(bytes));
    }, releaseLock() {} };
    const port = { writable: null, connected: true,
        async open() { state.opens++; this.writable = { getWriter: () => writer }; },
        async close() { state.closes++; this.writable = null; } };
    return { port, state };
}

function browser({ storage = new Map(), port = printerPort().port, cancelled = false, secure = true, parent = null } = {}) {
    const calls = [], requests = [];
    let devices = [
        { uid: 'other', name: 'Other printer', connection: 'driver' },
        { uid: 'zebra', name: 'ZDesigner ZT411-300dpi ZPL', connection: 'driver' }
    ];
    const window = { isSecureContext: secure };
    window.parent = parent || window;
    const context = { window, location: { protocol: 'https:', origin: 'https://mes.example' }, console, TextEncoder,
        localStorage: { getItem: key => storage.get(key) || null, setItem: (key, value) => storage.set(key, value) },
        navigator: { serial: { async requestPort(options) {
            requests.push(options);
            if (cancelled) throw Object.assign(new Error('Cancelled'), { name: 'NotFoundError' });
            return port;
        } } },
        fetch: async (url, options) => {
            calls.push({ url, options });
            return { ok: true, text: async () => url.endsWith('/available') ? JSON.stringify({ printer: devices })
                : url.includes('/default') ? JSON.stringify({ uid: 'other', name: 'Other printer' }) : '' };
        } };
    window.location = context.location;
    window.navigator = context.navigator;
    vm.runInNewContext(source, context);
    return { api: window, calls, requests, storage, setDevices: value => devices = value };
}

test('Portal fixes ZT411 ZPL by default; later explicit selection survives a fresh browser runtime', async () => {
    const b = browser();
    assert.equal((await b.api.amesZebraPrinters({ portalDefault: true })).defaultUid, 'zebra');
    assert.equal((await b.api.amesZebraSetDefault('other')).ok, true);
    const reopened = browser({ storage: b.storage });
    assert.equal((await reopened.api.amesZebraPrinters({ portalDefault: true })).defaultUid, 'other');
    // Existing callers outside Portal still use Browser Print's own default.
    assert.equal((await b.api.amesZebraPrinters()).defaultUid, 'other');
});

test('A missing saved printer remains selected but unavailable and cannot fall back to another device', async () => {
    const b = browser();
    await b.api.amesZebraPrinters({ portalDefault: true });
    b.setDevices([{ uid: 'other', name: 'Other printer' }]);
    const result = await b.api.amesZebraPrinters({ portalDefault: true, forceDiscovery: true });
    assert.equal(result.defaultUid, 'zebra');
    assert.equal(result.printers.find(p => p.uid === 'zebra').available, false);
    assert.equal((await b.api.amesZebraWrite('zebra', '^XA^XZ', true)).ok, false);
    assert.equal(b.calls.filter(c => c.url.endsWith('/write')).length, 0);
});

test('Bluetooth Find selects only SPP services, fixes the selected device, and sends no labels', async () => {
    const p = printerPort(), b = browser({ port: p.port });
    const found = await b.api.amesZebraBluetoothFind();
    assert.equal(found.ok, true);
    assert.equal(b.requests[0].filters[0].bluetoothServiceClassId, '00001101-0000-1000-8000-00805f9b34fb');
    assert.equal(p.state.attempts, 0);
    const devices = await b.api.amesZebraPrinters({ portalDefault: true });
    assert.equal(devices.defaultUid, found.printer.uid);
    assert.equal(devices.printers.find(p => p.uid === found.printer.uid).available, true);
    assert.equal(b.calls.length, 0); // Bluetooth does not require Browser Print to be installed.
});

test('Bluetooth print writes the exact ZPL in bounded chunks to only the selected port', async () => {
    const p = printerPort(), b = browser({ port: p.port });
    const found = await b.api.amesZebraBluetoothFind();
    const zpl = '^XA^CI28^FD' + 'A'.repeat(3500) + '^FS^PQ1^XZ';
    assert.equal((await b.api.amesZebraWrite(found.printer.uid, zpl, true)).ok, true);
    assert.ok(p.state.chunks.every(c => c.length <= 1024));
    assert.equal(Buffer.concat(p.state.chunks).toString('utf8'), zpl);
    assert.equal(p.state.opens, 2);
    assert.equal(p.state.closes, 2);
    assert.equal(b.calls.length, 0);
});

test('A browser reload never guesses which previously-authorized Bluetooth port to print to', async () => {
    const b = browser();
    const found = await b.api.amesZebraBluetoothFind();
    const reopened = browser({ storage: b.storage });
    const devices = await reopened.api.amesZebraPrinters({ portalDefault: true });
    assert.equal(devices.defaultUid, found.printer.uid);
    assert.equal(devices.printers.find(p => p.uid === found.printer.uid).available, false);
    assert.equal((await reopened.api.amesZebraWrite(found.printer.uid, '^XA^XZ', true)).ok, false);
    assert.equal(reopened.calls.filter(c => c.url.endsWith('/write')).length, 0);
});

test('Same-origin print previews share the fixed Bluetooth connection with the containing Portal page', async () => {
    const p = printerPort(), root = browser({ port: p.port });
    const frame = browser({ storage: root.storage, parent: root.api });
    const found = await frame.api.amesZebraBluetoothFind();
    assert.equal(found.ok, true);
    assert.equal(root.requests.length, 1);
    assert.equal(frame.requests.length, 0);
    assert.equal((await root.api.amesZebraPrinters({ portalDefault: true })).defaultUid, found.printer.uid);
    assert.equal((await root.api.amesZebraWrite(found.printer.uid, '^XA^XZ', true)).ok, true);
    assert.equal(p.state.attempts, 1);
});

test('Cancelling Bluetooth selection preserves the fixed printer and insecure pages cannot prompt', async () => {
    const storage = new Map();
    const b = browser({ storage });
    await b.api.amesZebraPrinters({ portalDefault: true });
    const saved = storage.get('ames.portal.label-printer');
    const cancelled = browser({ storage, cancelled: true });
    assert.equal((await cancelled.api.amesZebraBluetoothFind()).cancelled, true);
    assert.equal(storage.get('ames.portal.label-printer'), saved);
    const insecure = browser({ secure: false });
    assert.equal((await insecure.api.amesZebraBluetoothFind()).ok, false);
    assert.equal(insecure.requests.length, 0);
});

test('Bluetooth failure never retries a partially sent job', async () => {
    const p = printerPort({ failWrite: true }), b = browser({ port: p.port });
    const found = await b.api.amesZebraBluetoothFind();
    const result = await b.api.amesZebraWrite(found.printer.uid, '^XA^XZ', true);
    assert.equal(result.ok, false);
    assert.match(result.error, /Check the printer before retrying/);
    assert.equal(p.state.attempts, 1);
});

test('Concurrent Bluetooth jobs cannot interleave on one printer', async () => {
    let finish;
    const gate = new Promise(resolve => finish = resolve);
    const p = printerPort({ gate }), b = browser({ port: p.port });
    const found = await b.api.amesZebraBluetoothFind();
    const first = b.api.amesZebraWrite(found.printer.uid, '^XA^XZ', true);
    const second = await b.api.amesZebraWrite(found.printer.uid, '^XA^XZ', true);
    assert.equal(second.ok, false);
    finish();
    assert.equal((await first).ok, true);
    assert.equal(p.state.attempts, 1);
});
