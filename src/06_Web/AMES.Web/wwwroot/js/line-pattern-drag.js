'use strict';
// MD-28 LineTimePattern: Shift별 다중 타임라인 드래그.
// 여러 surface(각 Shift 창)를 지원하고, 드래그 종료 시
//   OnShiftBandDrag(shiftCode, startMin, endMin) 을 호출한다.
window.linePatternDrag = (() => {
    let _ref     = null;
    let _surfaces = [];   // { el, shift, startMin, rangeMin, down, ctx }
    let _active  = null;  // 현재 드래그 중인 surface
    let _anchor  = 0;
    let _preview = null;
    let _tooltip = null;
    let _move = null, _up = null, _key = null;

    function pxPerMin(s) { return s.el.getBoundingClientRect().width / s.rangeMin; }
    function pxToMin(s, px) { return s.startMin + Math.round(px / pxPerMin(s)); }
    function fmt(m) {
        m = Math.max(0, Math.min(1440, m));
        return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
    }

    function ensureTooltip() {
        if (_tooltip) return;
        _tooltip = document.createElement('div');
        Object.assign(_tooltip.style, {
            position: 'fixed', background: 'rgba(15,23,42,.93)', color: '#93c5fd',
            border: '1px solid rgba(59,130,246,.6)', borderRadius: '5px', padding: '4px 10px',
            fontSize: '12px', fontFamily: "'JetBrains Mono', monospace", pointerEvents: 'none',
            zIndex: '9999', whiteSpace: 'nowrap', boxShadow: '0 2px 10px rgba(0,0,0,.45)', userSelect: 'none',
        });
        document.body.appendChild(_tooltip);
    }
    function moveTooltip(cx, cy, lo, hi) {
        if (!_tooltip) return;
        _tooltip.textContent = `${fmt(lo)} – ${fmt(hi)}  (${Math.abs(hi - lo)}분)`;
        const tw = _tooltip.offsetWidth || 160, th = _tooltip.offsetHeight || 24;
        let tx = cx + 14, ty = cy - th - 8;
        if (tx + tw > window.innerWidth - 8) tx = cx - tw - 8;
        if (ty < 4) ty = cy + 14;
        _tooltip.style.left = tx + 'px'; _tooltip.style.top = ty + 'px';
    }
    function removeTooltip() { if (_tooltip) { _tooltip.remove(); _tooltip = null; } }
    function removePreview() { if (_preview) { _preview.remove(); _preview = null; } }
    function cancel() { _active = null; removePreview(); removeTooltip(); }

    return {
        init(dotNetRef, surfaces) {
            _ref = dotNetRef;
            this.destroy();
            (surfaces || []).forEach(cfg => {
                const el = document.getElementById(cfg.id);
                if (!el) return;
                const s = { el, shift: cfg.shift, startMin: cfg.startMin, rangeMin: cfg.rangeMin };
                s.down = e => {
                    if (e.button !== 0) return;
                    e.preventDefault();
                    removePreview();               // 잔여 미리보기 제거 (연속 드래그 안전)
                    _active = s;
                    const rect = el.getBoundingClientRect();
                    const px = Math.max(0, e.clientX - rect.left);
                    _anchor = pxToMin(s, px);
                    // 미리보기는 body에 fixed로 부착 — Blazor가 관리하는 surface DOM과 격리해 잔상 방지
                    _preview = document.createElement('div');
                    Object.assign(_preview.style, {
                        position: 'fixed', top: (rect.top + 4) + 'px', height: (rect.height - 8) + 'px',
                        left: (rect.left + px) + 'px', width: '2px', background: 'rgba(59,130,246,.35)',
                        border: '1px solid rgba(59,130,246,.85)', borderRadius: '3px',
                        pointerEvents: 'none', zIndex: '9998',
                    });
                    document.body.appendChild(_preview);
                    ensureTooltip();
                    moveTooltip(e.clientX, e.clientY, _anchor, _anchor);
                };
                s.ctx = e => { if (_active) { e.preventDefault(); cancel(); } };
                el.addEventListener('mousedown', s.down);
                el.addEventListener('contextmenu', s.ctx);
                _surfaces.push(s);
            });

            _move = e => {
                if (!_active || !_preview) return;
                const s = _active;
                const rect = s.el.getBoundingClientRect();
                const px = Math.max(0, Math.min(e.clientX - rect.left, rect.width));
                const cur = pxToMin(s, px);
                const lo = Math.min(_anchor, cur), hi = Math.max(_anchor, cur);
                const ppm = pxPerMin(s);
                _preview.style.left = (rect.left + (lo - s.startMin) * ppm) + 'px';
                _preview.style.top = (rect.top + 4) + 'px';
                _preview.style.height = (rect.height - 8) + 'px';
                _preview.style.width = Math.max(2, (hi - lo) * ppm) + 'px';
                moveTooltip(e.clientX, e.clientY, lo, hi);
            };
            _up = e => {
                if (!_active) return;
                const s = _active;
                const rect = s.el.getBoundingClientRect();
                const px = Math.max(0, Math.min(e.clientX - rect.left, rect.width));
                const endMin = pxToMin(s, px);
                const lo = Math.min(_anchor, endMin), hi = Math.max(_anchor, endMin);
                const shift = s.shift;
                cancel();
                if (hi > lo && _ref) _ref.invokeMethodAsync('OnShiftBandDrag', shift, lo, hi);
            };
            _key = e => { if (e.key === 'Escape' && _active) cancel(); };
            document.addEventListener('mousemove', _move);
            document.addEventListener('mouseup', _up);
            document.addEventListener('keydown', _key);
        },

        destroy() {
            _surfaces.forEach(s => {
                if (s.down) s.el.removeEventListener('mousedown', s.down);
                if (s.ctx) s.el.removeEventListener('contextmenu', s.ctx);
            });
            _surfaces = [];
            if (_move) document.removeEventListener('mousemove', _move);
            if (_up) document.removeEventListener('mouseup', _up);
            if (_key) document.removeEventListener('keydown', _key);
            _move = _up = _key = null;
            cancel();
        }
    };
})();
