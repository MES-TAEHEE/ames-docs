// Zebra Browser Print(이 PC 에 설치하는 Zebra 무료 에이전트)로 ZPL 을 PC 의 Zebra 프린터에 바로 보낸다.
// 드라이버 용지 설정을 거치지 않아 ^PW/^LL 그대로 나온다(MD-034 팔레트 라벨). 에이전트는 http://127.0.0.1:9100 ·
// https://127.0.0.1:9101 에서 듣는다 — https 화면은 혼합 콘텐츠라 9101 을 쓰고, 첫 사용 때 그 주소의 인증서를 한 번 허용해야 한다.
// 요청은 Zebra SDK(BrowserPrint.js)와 같이 text/plain 으로 보낸다(사전 요청 없이 통과).
(function () {
    const hosts = () => location.protocol === 'https:'
        ? ['https://127.0.0.1:9101', 'https://localhost:9101']
        : ['http://127.0.0.1:9100', 'https://127.0.0.1:9101'];

    // 실패 원인을 주소마다 남긴다 — 화면에 그대로 보여 막힌 곳(미설치·인증서·브라우저 차단)을 가린다
    async function call(path, body) {
        const errs = [];
        for (const h of hosts()) {
            try {
                const opt = body === undefined
                    ? { method: 'GET' }
                    : { method: 'POST', headers: { 'Content-Type': 'text/plain;charset=utf-8' }, body: body };
                opt.targetAddressSpace = 'loopback';   // Chrome 로컬 네트워크 접근(LNA) — 다른 브라우저는 무시
                const r = await fetch(h + path, opt);
                if (!r.ok) { errs.push(h + ' → HTTP ' + r.status); continue; }
                const t = await r.text();
                return { ok: true, text: t };
            } catch (e) { errs.push(h + ' → ' + String(e && e.message || e)); }
        }
        console.warn('[zebra-print]', path, errs);
        return { ok: false, error: errs.join(' / ') };
    }

    const parse = t => { try { return t ? JSON.parse(t) : null; } catch { return null; } };
    const view = d => d && d.uid ? { uid: d.uid, name: d.name || d.uid, connection: d.connection || '' } : null;

    // 프린터 목록과 기본 프린터 — { ok, printers:[{uid,name,connection}], defaultUid, error }
    window.amesZebraPrinters = async function () {
        const avail = await call('/available');
        if (!avail.ok) return { ok: false, printers: [], defaultUid: null, error: avail.error };
        const a = parse(avail.text) || {};
        const printers = (a.printer || []).map(view).filter(x => x);
        const def = await call('/default?type=printer');
        const d = def.ok ? view(parse(def.text)) : null;
        if (d && !printers.some(p => p.uid === d.uid)) printers.unshift(d);
        return { ok: true, printers: printers, defaultUid: d ? d.uid : (printers[0] ? printers[0].uid : null), error: null };
    };

    // ZPL 보내기 — { ok, error }. uid 로 고른 프린터(없으면 기본 프린터)
    window.amesZebraWrite = async function (uid, zpl) {
        let device = null;
        const avail = await call('/available');
        if (avail.ok) device = ((parse(avail.text) || {}).printer || []).find(p => p.uid === uid) || null;
        if (!device) {
            const def = await call('/default?type=printer');
            if (!def.ok) return { ok: false, error: def.error };
            device = parse(def.text);
        }
        if (!device || !device.uid) return { ok: false, error: 'no-printer' };
        const w = await call('/write', JSON.stringify({ device: device, data: zpl }));
        return w.ok ? { ok: true, error: null } : { ok: false, error: w.error };
    };
})();
