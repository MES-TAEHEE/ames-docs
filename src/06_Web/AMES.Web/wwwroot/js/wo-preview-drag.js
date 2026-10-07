'use strict';
// PP-APS WO 미리보기 보드 — 이번 생성 슬롯(.wp-blk.draggable)을 같은 줄 안에서 좌우로 끌어 시각을 옮긴다(5분 스냅).
// 놓으면 .NET OnSlotDragged(key, newStartMin) 로 알리고, 검증·다시 그리기는 Blazor 가 한다. 끌었던 클릭은 상세 팝업을 열지 않게 삼킨다.
window.woPreviewDrag = (() => {
    let _ref = null, _root = null, _drag = null, _tip = null, _suppress = false;
    const h = {};
    const fmt = m => { m = ((m % 1440) + 1440) % 1440; return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`; };

    function ensureTip() {
        if (_tip) return;
        _tip = document.createElement('div');
        Object.assign(_tip.style, {
            position: 'fixed', zIndex: '9999', pointerEvents: 'none', padding: '4px 10px', borderRadius: '5px',
            fontSize: '12px', fontFamily: "'JetBrains Mono', monospace", whiteSpace: 'nowrap',
            background: 'rgba(15,23,42,.93)', color: '#d8b4fe', border: '1px solid rgba(216,180,254,.7)',
            boxShadow: '0 2px 10px rgba(0,0,0,.45)', userSelect: 'none',
        });
        document.body.appendChild(_tip);
    }
    function finish() {
        if (!_drag) return null;
        _drag.el.style.transform = '';
        _drag.el.classList.remove('dragging');
        if (_tip) { _tip.remove(); _tip = null; }
        const d = _drag; _drag = null; return d;
    }

    return {
        init(dotNetRef, rootId) {
            this.destroy();
            _ref = dotNetRef;
            _root = document.getElementById(rootId);
            if (!_root) return;
            h.down = e => {
                if (e.button !== 0) return;
                const el = e.target.closest('.wp-blk.draggable');
                if (!el || !_root.contains(el)) return;
                const row = el.closest('.wp-row');
                if (!row) return;
                e.preventDefault();
                _drag = { el, row, x0: e.clientX, start: +el.dataset.start, end: +el.dataset.end, dayStart: +el.dataset.daystart, key: el.dataset.key, delta: 0, moved: false };
            };
            h.move = e => {
                if (!_drag) return;
                const ppm = _drag.row.getBoundingClientRect().width / 1440;
                const dur = _drag.end - _drag.start;
                const rel = (((_drag.start - _drag.dayStart) % 1440) + 1440) % 1440;
                let dmin = Math.round((e.clientX - _drag.x0) / ppm / 5) * 5;
                dmin = Math.max(-rel, Math.min(1440 - dur - rel, dmin));
                if (Math.abs(e.clientX - _drag.x0) > 3) _drag.moved = true;
                _drag.delta = dmin;
                _drag.el.style.transform = `translateX(${dmin * ppm}px)`;
                _drag.el.classList.add('dragging');
                ensureTip();
                const s = _drag.start + dmin;
                _tip.textContent = `${fmt(s)} – ${fmt(s + dur)}`;
                _tip.style.left = (e.clientX + 14) + 'px';
                _tip.style.top  = (e.clientY - 30) + 'px';
            };
            h.up = () => {
                const d = finish();
                if (!d || !d.moved) return;
                _suppress = true;
                setTimeout(() => { _suppress = false; }, 0);
                if (d.delta !== 0 && _ref) _ref.invokeMethodAsync('OnSlotDragged', d.key, d.start + d.delta);
            };
            h.click = e => { if (_suppress) { e.stopPropagation(); e.preventDefault(); _suppress = false; } };
            h.key = e => { if (e.key === 'Escape' && _drag) finish(); };
            _root.addEventListener('mousedown', h.down);
            _root.addEventListener('click', h.click, true);
            document.addEventListener('mousemove', h.move);
            document.addEventListener('mouseup', h.up);
            document.addEventListener('keydown', h.key);
        },
        destroy() {
            if (_root) {
                if (h.down)  _root.removeEventListener('mousedown', h.down);
                if (h.click) _root.removeEventListener('click', h.click, true);
            }
            if (h.move) document.removeEventListener('mousemove', h.move);
            if (h.up)   document.removeEventListener('mouseup', h.up);
            if (h.key)  document.removeEventListener('keydown', h.key);
            h.down = h.click = h.move = h.up = h.key = null;
            finish();
            _root = null; _ref = null;
        },
    };
})();
