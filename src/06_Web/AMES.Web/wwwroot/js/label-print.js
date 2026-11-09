// 라벨만 담은 숨은 인쇄 프레임에서 브라우저 인쇄 창을 연다 — 본 창에서 인쇄하면 다른 화면의 @page 규칙(Letter·100×50mm 등)이
// 용지 크기를 덮는다. 프레임에는 app.css(라벨 모양)와 이 라벨의 용지 규칙만 둔다. 프린터·용지는 이 PC 의 인쇄 설정을 따른다
// (Zebra Browser Print 가 없는 PC 용 — MD-034 출력 창에서 고른다).
window.amesPrintLabels = function (selector, pageSize) {
    const src = document.querySelector(selector);
    if (!src) return false;
    const css = [...document.querySelectorAll('link[rel="stylesheet"]')]
        .map(l => l.href).filter(h => /\/app(\.[\w-]+)?\.css/.test(h));
    const old = document.getElementById('ames-label-print-frame');
    if (old) old.remove();
    const f = document.createElement('iframe');
    f.id = 'ames-label-print-frame';
    f.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden;';
    document.body.appendChild(f);
    const d = f.contentDocument;
    d.open();
    d.write('<!doctype html><html><head><meta charset="utf-8">' +
        css.map(h => `<link rel="stylesheet" href="${h}">`).join('') +
        `<style>@page { size: ${pageSize}; margin: 0; }
html, body { margin: 0; padding: 0; background: #fff; }
body * { visibility: visible !important; }
.pallet-print-sheet { display: block !important; }
.pallet-label-paper { break-after: page; }
.pallet-label-paper:last-child { break-after: auto; }</style></head><body>` +
        src.outerHTML + '</body></html>');
    d.close();
    const go = () => { f.contentWindow.focus(); f.contentWindow.print(); };
    const links = [...d.querySelectorAll('link')];
    let left = links.length, done = false;
    const once = () => { if (!done) { done = true; setTimeout(go, 50); } };
    if (left === 0) once();
    links.forEach(l => { l.onload = l.onerror = () => { if (--left <= 0) once(); }; });
    setTimeout(once, 1500);
    return true;
};

// 출력 방식 기억(이 PC·이 브라우저) — 저장소가 막혀 있어도 화면은 동작해야 하므로 실패는 무시한다
window.amesLabelPref = {
    get: function (key) { try { return localStorage.getItem(key); } catch { return null; } },
    set: function (key, value) { try { localStorage.setItem(key, value); } catch { } }
};
