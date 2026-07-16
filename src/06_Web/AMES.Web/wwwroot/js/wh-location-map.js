'use strict';

window.whLocationMap = (() => {
    let _ref = null;
    let _plan = null;
    let _active = null;
    let _suppressClick = false;
    const h = {};

    const minSizePct = 8;

    function clamp(value, min, max) {
        return Math.max(min, Math.min(max, value));
    }

    function roundPct(value) {
        return Math.round(value * 100) / 100;
    }

    function readPct(block) {
        const planRect = _plan.getBoundingClientRect();
        const blockRect = block.getBoundingClientRect();
        return {
            x: ((blockRect.left - planRect.left) / planRect.width) * 100,
            y: ((blockRect.top - planRect.top) / planRect.height) * 100,
            w: (blockRect.width / planRect.width) * 100,
            h: (blockRect.height / planRect.height) * 100,
        };
    }

    function applyPct(block, pct) {
        block.style.left = roundPct(pct.x) + '%';
        block.style.top = roundPct(pct.y) + '%';
        block.style.width = roundPct(pct.w) + '%';
        block.style.height = roundPct(pct.h) + '%';
    }

    function clearActiveClasses() {
        if (!_active?.block) return;
        _active.block.classList.remove('dragging', 'resizing');
    }

    function cancel() {
        clearActiveClasses();
        _active = null;
        document.body.classList.remove('wh-map-editing');
    }

    function onPointerDown(e) {
        if (e.button !== undefined && e.button !== 0) return;
        const block = e.target.closest('.wh-area-block');
        if (!_plan || !block || !_plan.contains(block)) return;

        const areaCode = block.dataset.areaCode;
        if (!areaCode) return;

        const mode = e.target.closest('.wh-area-resize-handle') ? 'resize' : 'drag';
        const start = readPct(block);

        _active = {
            mode,
            block,
            areaCode,
            start,
            startClientX: e.clientX,
            startClientY: e.clientY,
            moved: false,
        };

        block.classList.add(mode === 'resize' ? 'resizing' : 'dragging');
        document.body.classList.add('wh-map-editing');

        try { block.setPointerCapture?.(e.pointerId); } catch { }

        e.preventDefault();
        e.stopPropagation();
    }

    function onPointerMove(e) {
        if (!_active || !_plan) return;

        const planRect = _plan.getBoundingClientRect();
        if (planRect.width <= 0 || planRect.height <= 0) return;

        const dxPx = e.clientX - _active.startClientX;
        const dyPx = e.clientY - _active.startClientY;
        if (Math.abs(dxPx) > 2 || Math.abs(dyPx) > 2) {
            _active.moved = true;
        }

        const dx = (dxPx / planRect.width) * 100;
        const dy = (dyPx / planRect.height) * 100;
        const next = { ..._active.start };

        if (_active.mode === 'resize') {
            next.w = clamp(_active.start.w + dx, minSizePct, 100 - _active.start.x);
            next.h = clamp(_active.start.h + dy, minSizePct, 100 - _active.start.y);
        } else {
            next.x = clamp(_active.start.x + dx, 0, 100 - _active.start.w);
            next.y = clamp(_active.start.y + dy, 0, 100 - _active.start.h);
        }

        applyPct(_active.block, next);
        e.preventDefault();
    }

    function onPointerUp(e) {
        if (!_active) return;

        const finished = _active;
        const finalPct = readPct(finished.block);
        const changed = finished.moved;

        cancel();

        if (changed) {
            _suppressClick = true;
            window.setTimeout(() => { _suppressClick = false; }, 0);
            _ref?.invokeMethodAsync(
                'OnAreaLayoutChanged',
                finished.areaCode,
                roundPct(finalPct.x),
                roundPct(finalPct.y),
                roundPct(finalPct.w),
                roundPct(finalPct.h));
        } else {
            _ref?.invokeMethodAsync('OnAreaSelectedFromMap', finished.areaCode);
        }

        e.preventDefault();
        e.stopPropagation();
    }

    function onClick(e) {
        if (!_suppressClick) return;
        const block = e.target.closest('.wh-area-block');
        if (!block || !_plan?.contains(block)) return;
        e.preventDefault();
        e.stopPropagation();
    }

    function onKeyDown(e) {
        if (e.key === 'Escape' && _active) {
            cancel();
        }
    }

    return {
        init(dotnetRef, planId) {
            this.destroy();
            _ref = dotnetRef;
            _plan = document.getElementById(planId);
            if (!_plan) return;

            h.down = onPointerDown;
            h.move = onPointerMove;
            h.up = onPointerUp;
            h.click = onClick;
            h.key = onKeyDown;

            _plan.addEventListener('pointerdown', h.down);
            _plan.addEventListener('click', h.click, true);
            document.addEventListener('pointermove', h.move);
            document.addEventListener('pointerup', h.up);
            document.addEventListener('keydown', h.key);
        },

        destroy() {
            if (_plan) {
                if (h.down) _plan.removeEventListener('pointerdown', h.down);
                if (h.click) _plan.removeEventListener('click', h.click, true);
            }

            if (h.move) document.removeEventListener('pointermove', h.move);
            if (h.up) document.removeEventListener('pointerup', h.up);
            if (h.key) document.removeEventListener('keydown', h.key);

            h.down = h.move = h.up = h.click = h.key = null;
            cancel();
            _plan = null;
            _ref = null;
            _suppressClick = false;
        }
    };
})();
