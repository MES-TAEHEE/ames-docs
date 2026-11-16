// Zebra Browser Print(이 PC 에 설치하는 Zebra 무료 에이전트)로 ZPL 을 PC 의 Zebra 프린터에 바로 보낸다.
// 드라이버 용지 설정을 거치지 않아 ^PW/^LL 그대로 나온다(MD-034 팔레트 라벨). 에이전트는 http://127.0.0.1:9100 ·
// https://127.0.0.1:9101 에서 듣는다 — https 화면은 혼합 콘텐츠라 9101 을 쓰고, 첫 사용 때 그 주소의 인증서를 한 번 허용해야 한다.
// 요청은 Zebra SDK(BrowserPrint.js)와 같이 text/plain 으로 보낸다(사전 요청 없이 통과).
(function () {
    const DEFAULT_KEY = 'ames.portal.label-printer';
    const SPP = '00001101-0000-1000-8000-00805f9b34fb';
    let knownPrinters = [];
    let owner = window;
    try { while (owner.parent && owner.parent !== owner && owner.parent.location.origin === location.origin) owner = owner.parent; } catch { }
    const bluetooth = owner.amesZebraBluetoothPorts ||= { ports: new Map(), next: 0 };
    const savedPrinter = () => { try { return JSON.parse(localStorage.getItem(DEFAULT_KEY) || 'null'); } catch { return null; } };
    const remember = printer => { try { localStorage.setItem(DEFAULT_KEY, JSON.stringify(printer)); return true; } catch { return false; } };
    const hosts = () => location.protocol === 'https:'
        ? ['https://127.0.0.1:9101', 'https://localhost:9101']
        : ['http://127.0.0.1:9100', 'https://127.0.0.1:9101'];

    // 실패 원인을 주소마다 남긴다 — 화면에 그대로 보여 막힌 곳(미설치·인증서·브라우저 차단)을 가린다
    let serviceHost;
    async function call(path, body) {
        const errs = [];
        // A failed write may already have printed; never resend it to another address.
        for (const h of body === undefined ? hosts() : [serviceHost || hosts()[0]]) {
            try {
                const opt = body === undefined
                    ? { method: 'GET' }
                    : { method: 'POST', headers: { 'Content-Type': 'text/plain;charset=utf-8' }, body: body };
                opt.targetAddressSpace = 'loopback';   // Chrome 로컬 네트워크 접근(LNA) — 다른 브라우저는 무시
                if (typeof AbortSignal !== 'undefined' && AbortSignal.timeout) opt.signal = AbortSignal.timeout(body === undefined ? 2500 : 15000);
                const r = await fetch(h + path, opt);
                if (!r.ok) { errs.push(h + ' → HTTP ' + r.status); continue; }
                const t = await r.text();
                serviceHost = h;
                return { ok: true, text: t };
            } catch (e) { errs.push(h + ' → ' + String(e && e.message || e)); }
        }
        console.warn('[zebra-print]', path, errs);
        return { ok: false, error: errs.join(' / ') };
    }

    const parse = t => { try { return t ? JSON.parse(t) : null; } catch { return null; } };
    const view = d => d && d.uid ? { uid: d.uid, name: d.name || d.uid, connection: d.connection || '' } : null;

    // 프린터 목록과 기본 프린터 — { ok, printers:[{uid,name,connection}], defaultUid, error }
    window.amesZebraPrinters = async function (options = {}) {
        const portal = options.portalDefault === true;
        const saved = portal ? savedPrinter() : null;
        let printers = knownPrinters.filter(p => p.connection !== 'bluetooth-serial');
        let d = null, error = null;
        const savedBluetooth = saved && bluetooth.ports.get(saved.uid);
        if (!portal || options.forceDiscovery || (!printers.length && !savedBluetooth)) {
            const avail = await call('/available');
            if (avail.ok) {
                printers = ((parse(avail.text) || {}).printer || []).map(view).filter(x => x);
                const def = await call('/default?type=printer');
                d = def.ok ? view(parse(def.text)) : null;
                if (d && !printers.some(p => p.uid === d.uid)) printers.unshift(d);
            } else error = avail.error;
        }
        if (portal) {
            for (const [uid, entry] of bluetooth.ports) {
                if (!printers.some(p => p.uid === uid)) printers.push({ uid, name: entry.name, connection: 'bluetooth-serial', available: entry.port.connected !== false });
            }
            if (saved && !printers.some(p => p.uid === saved.uid)) printers.unshift({ ...saved, available: false });
        }
        knownPrinters = printers;
        const preferred = saved || (portal && (printers.find(p => /ZT411/i.test(p.name) && /ZPL/i.test(p.name)) || printers.find(p => /ZT411/i.test(p.name)))) || d || printers.find(p => p.available !== false);
        if (portal && !saved && preferred) remember(preferred);
        if (saved && !printers.some(p => p.uid === saved.uid && p.available !== false)) error = 'Saved printer is unavailable. Reconnect it or select another printer.';
        else if (printers.some(p => p.available !== false)) error = null;
        return { ok: printers.some(p => p.available !== false), printers, defaultUid: preferred ? preferred.uid : null, error };
    };

    window.amesZebraSetDefault = async function (uid) {
        const printer = knownPrinters.find(p => p.uid === uid && p.available !== false);
        if (!printer) return { ok: false, error: 'Selected printer is unavailable.' };
        return remember(printer) ? { ok: true, error: null } : { ok: false, error: 'Browser storage is unavailable. The default printer could not be saved.' };
    };

    window.amesZebraBluetoothFind = async function () {
        if (!window.isSecureContext || typeof navigator === 'undefined' || !navigator.serial)
            return { ok: false, cancelled: false, error: 'Bluetooth printing requires desktop Chrome/Edge over HTTPS or localhost. Pair the printer in the computer Bluetooth settings first.' };
        try {
            // requestPort must run before any asynchronous work to preserve the user's click activation.
            const serial = (owner.navigator && owner.navigator.serial) || navigator.serial;
            const port = await serial.requestPort({ filters: [{ bluetoothServiceClassId: SPP }] });
            if (!port.writable) await port.open({ baudRate: 9600 });
            await port.close();
            const uid = 'bt:' + Date.now() + ':' + (++bluetooth.next);
            const printer = { uid, name: 'Bluetooth printer (SPP)', connection: 'bluetooth-serial', available: true };
            bluetooth.ports.clear();
            bluetooth.ports.set(uid, { port, name: printer.name, writing: false });
            knownPrinters = knownPrinters.filter(p => p.connection !== 'bluetooth-serial').concat(printer);
            if (!remember(printer)) return { ok: false, cancelled: false, error: 'Browser storage is unavailable. The default printer could not be saved.' };
            return { ok: true, cancelled: false, printer, error: null };
        } catch (e) {
            return { ok: false, cancelled: e && e.name === 'NotFoundError', error: String(e && e.message || e) };
        }
    };

    async function writeBluetooth(uid, zpl) {
        const entry = bluetooth.ports.get(uid);
        if (!entry) return { ok: false, error: 'Select the saved Bluetooth printer again using Bluetooth Find.' };
        if (entry.writing) return { ok: false, error: 'This Bluetooth printer is already printing.' };
        entry.writing = true;
        let writer, attempted = false;
        try {
            if (!entry.port.writable) await entry.port.open({ baudRate: 9600 });
            writer = entry.port.writable.getWriter();
            const bytes = new TextEncoder().encode(zpl);
            for (let i = 0; i < bytes.length; i += 1024) { attempted = true; await writer.write(bytes.slice(i, i + 1024)); }
            return { ok: true, error: null };
        } catch (e) {
            return { ok: false, error: String(e && e.message || e) + (attempted ? ' Check the printer before retrying.' : '') };
        } finally {
            if (writer) writer.releaseLock();
            try { if (entry.port.writable) await entry.port.close(); } catch { }
            entry.writing = false;
        }
    }

    // ZPL 보내기 — { ok, error }. uid 로 고른 프린터(없으면 기본 프린터)
    window.amesZebraWrite = async function (uid, zpl, requireSelected = false) {
        if (uid && uid.startsWith('bt:')) return writeBluetooth(uid, zpl);
        let device = null;
        const avail = await call('/available');
        if (avail.ok) device = ((parse(avail.text) || {}).printer || []).find(p => p.uid === uid) || null;
        if (!device) {
            if (requireSelected) return { ok: false, error: 'Selected printer is unavailable.' };
            const def = await call('/default?type=printer');
            if (!def.ok) return { ok: false, error: def.error };
            device = parse(def.text);
        }
        if (!device || !device.uid) return { ok: false, error: 'no-printer' };
        const w = await call('/write', JSON.stringify({ device: device, data: zpl }));
        return w.ok ? { ok: true, error: null } : { ok: false, error: w.error };
    };
})();
