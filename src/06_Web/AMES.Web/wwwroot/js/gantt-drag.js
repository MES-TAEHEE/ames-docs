'use strict';
window.ganttDrag = (() => {
    let _ref       = null;
    let _surface   = null;
    let _startMin  = 360;   // display range start (minutes, e.g. 06:00 = 360)
    let _rangeMin  = 960;   // display range width in minutes (e.g. 16h = 960)
    let _dragging  = false;
    let _dragAnchor = 0;    // minute where drag started
    let _preview   = null;
    let _tooltip   = null;
    const h = {};

    // 1px = rangeMin / surfaceWidth minutes  →  inverse for pxPerMin
    function pxPerMin() {
        return _surface ? _surface.getBoundingClientRect().width / _rangeMin : 1;
    }

    function pxToMin(px) {
        return _startMin + Math.round(px / pxPerMin());
    }

    function minToPx(m) {
        return (m - _startMin) * pxPerMin();
    }

    function fmt(m) {
        m = Math.max(0, Math.min(1439, m));
        return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
    }

    // ── tooltip ─────────────────────────────────────────────────────────
    function ensureTooltip() {
        if (_tooltip) return;
        _tooltip = document.createElement('div');
        Object.assign(_tooltip.style, {
            position:    'fixed',
            background:  'rgba(15,23,42,.93)',
            color:       '#93c5fd',
            border:      '1px solid rgba(59,130,246,.6)',
            borderRadius:'5px',
            padding:     '4px 10px',
            fontSize:    '12px',
            fontFamily:  "'JetBrains Mono', monospace",
            pointerEvents:'none',
            zIndex:      '9999',
            whiteSpace:  'nowrap',
            boxShadow:   '0 2px 10px rgba(0,0,0,.45)',
            userSelect:  'none',
        });
        document.body.appendChild(_tooltip);
    }

    function moveTooltip(clientX, clientY, lo, hi) {
        if (!_tooltip) return;
        const dur  = hi - lo;
        const sign = dur < 0 ? '' : '';
        _tooltip.textContent = `${fmt(lo)} – ${fmt(hi)}  (${Math.abs(dur)}분)`;
        // keep tooltip inside viewport
        const tw = _tooltip.offsetWidth  || 160;
        const th = _tooltip.offsetHeight || 24;
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        let tx = clientX + 14;
        let ty = clientY - th - 8;
        if (tx + tw > vw - 8) tx = clientX - tw - 8;
        if (ty < 4) ty = clientY + 14;
        _tooltip.style.left = tx + 'px';
        _tooltip.style.top  = ty + 'px';
    }

    function removeTooltip() {
        if (_tooltip) { _tooltip.remove(); _tooltip = null; }
    }

    // ── preview bar ─────────────────────────────────────────────────────
    function removePreview() {
        if (_preview) { _preview.remove(); _preview = null; }
    }

    // ── cancel in-progress drag ──────────────────────────────────────────
    function cancelDrag() {
        _dragging = false;
        removePreview();
        removeTooltip();
    }

    return {
        init(dotNetRef, surfaceId, startMin, rangeMin) {
            _ref      = dotNetRef;
            _startMin = startMin ?? 360;
            _rangeMin = rangeMin ?? 960;

            this.destroy();   // detach any previous listeners

            _surface = document.getElementById(surfaceId);
            if (!_surface) return;

            // ── mousedown: begin drag ────────────────────────────────────
            h.down = e => {
                if (e.button !== 0) return;
                e.preventDefault();
                const rect = _surface.getBoundingClientRect();
                const px   = Math.max(0, e.clientX - rect.left);
                _dragAnchor = pxToMin(px);
                _dragging   = true;

                _preview = document.createElement('div');
                Object.assign(_preview.style, {
                    position:      'absolute',
                    top:           '4px',
                    height:        'calc(100% - 8px)',
                    left:          px + 'px',
                    width:         '2px',
                    background:    'rgba(59,130,246,.35)',
                    border:        '1px solid rgba(59,130,246,.85)',
                    borderRadius:  '3px',
                    pointerEvents: 'none',
                    zIndex:        '30',
                });
                _surface.appendChild(_preview);

                ensureTooltip();
                moveTooltip(e.clientX, e.clientY, _dragAnchor, _dragAnchor);
            };

            // ── mousemove: update preview + tooltip ──────────────────────
            h.move = e => {
                if (!_dragging || !_preview) return;
                const rect = _surface.getBoundingClientRect();
                const px   = Math.max(0, Math.min(e.clientX - rect.left, rect.width));
                const cur  = pxToMin(px);
                const lo   = Math.min(_dragAnchor, cur);
                const hi   = Math.max(_dragAnchor, cur);
                const ppm  = pxPerMin();
                _preview.style.left  = ((lo - _startMin) * ppm) + 'px';
                _preview.style.width = Math.max(2, (hi - lo) * ppm) + 'px';
                moveTooltip(e.clientX, e.clientY, lo, hi);
            };

            // ── mouseup: commit or ignore ────────────────────────────────
            h.up = e => {
                if (!_dragging) return;
                const rect   = _surface.getBoundingClientRect();
                const px     = Math.max(0, Math.min(e.clientX - rect.left, rect.width));
                const endMin = pxToMin(px);
                const s  = Math.min(_dragAnchor, endMin);
                const en = Math.max(_dragAnchor, endMin);
                cancelDrag();
                if (en > s && _ref) {
                    _ref.invokeMethodAsync('OnGanttDrag', s, en);
                }
            };

            // ── Escape: cancel drag ──────────────────────────────────────
            h.key = e => {
                if (e.key === 'Escape' && _dragging) cancelDrag();
            };

            // ── right-click: cancel drag ─────────────────────────────────
            h.ctx = e => {
                if (_dragging) { e.preventDefault(); cancelDrag(); }
            };

            _surface.addEventListener('mousedown',   h.down);
            _surface.addEventListener('contextmenu', h.ctx);
            document.addEventListener('mousemove',   h.move);
            document.addEventListener('mouseup',     h.up);
            document.addEventListener('keydown',     h.key);
        },

        destroy() {
            if (_surface) {
                if (h.down) _surface.removeEventListener('mousedown',   h.down);
                if (h.ctx)  _surface.removeEventListener('contextmenu', h.ctx);
            }
            if (h.move) document.removeEventListener('mousemove', h.move);
            if (h.up)   document.removeEventListener('mouseup',   h.up);
            if (h.key)  document.removeEventListener('keydown',   h.key);
            h.down = h.move = h.up = h.key = h.ctx = null;
            cancelDrag();
            _surface = null;
        }
    };
})();
