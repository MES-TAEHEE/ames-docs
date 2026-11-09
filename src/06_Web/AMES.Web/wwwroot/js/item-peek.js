// 품번 BOM 미리보기 — 화면은 품번 글자에 data-item="품번" 만 붙인다. 누르면 레이아웃의 ItemBomPeek 가 팝업을 연다.
// window 캡처 단계에서 가로채 행 클릭(수정 창·행 펼침·팝업 배경 닫기)으로 넘어가지 않게 한다.
// 등록된 컴포넌트가 없으면(외부 포탈·로그인 화면) 손대지 않아 평소 클릭 그대로다.
(() => {
    let peek = null, token = null;
    window.amesItemPeek = {
        register: function (ref, t) { peek = ref; token = t; },
        unregister: function (t) { if (token === t) { peek = null; token = null; } }
    };
    window.addEventListener('click', e => {
        if (!peek || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey) return;
        const el = e.target && e.target.closest ? e.target.closest('[data-item]') : null;
        if (!el) return;
        const item = (el.getAttribute('data-item') || '').trim();
        if (!item) return;
        e.preventDefault();
        e.stopPropagation();
        peek.invokeMethodAsync('Open', item).catch(() => { });
    }, true);

    // 팝업 머리(.ames-modal-h)를 잡고 끌어 옮긴다 — transform 만 바꿔 레이아웃을 다시 계산하지 않고, 프레임마다 한 번만 그린다.
    // 위치는 그 팝업 요소의 inline style 에만 남아 닫았다 다시 열면(요소를 새로 그림) 가운데로 돌아온다.
    let drag = null;
    function paint() {
        if (!drag) return;
        drag.raf = 0;
        drag.box.style.transform = `translate3d(${drag.x}px, ${drag.y}px, 0)`;
    }
    window.addEventListener('pointerdown', e => {
        if (e.button !== 0) return;
        const head = e.target.closest ? e.target.closest('.ipk > .ames-modal-h') : null;
        if (!head || e.target.closest('button, a, [data-item]')) return;
        const box = head.parentElement;
        const m = /translate3d\((-?[\d.]+)px, (-?[\d.]+)px/.exec(box.style.transform || '');
        const r = box.getBoundingClientRect();
        drag = { box, head, id: e.pointerId, sx: e.clientX, sy: e.clientY,
                 bx: m ? +m[1] : 0, by: m ? +m[2] : 0, x: 0, y: 0, raf: 0,
                 // 머리가 화면 밖으로 완전히 나가 다시 잡을 수 없게 되지 않도록 이동 범위를 둔다
                 minX: -r.left - r.width + 80, maxX: window.innerWidth - r.left - 80,
                 minY: -r.top, maxY: window.innerHeight - r.top - 40 };
        drag.x = drag.bx; drag.y = drag.by;
        try { head.setPointerCapture(e.pointerId); } catch { }
        box.classList.add('ipk-dragging');
        e.preventDefault();
    });
    window.addEventListener('pointermove', e => {
        if (!drag || e.pointerId !== drag.id) return;
        const dx = Math.min(Math.max(e.clientX - drag.sx, drag.minX), drag.maxX);
        const dy = Math.min(Math.max(e.clientY - drag.sy, drag.minY), drag.maxY);
        drag.x = drag.bx + dx; drag.y = drag.by + dy;
        if (!drag.raf) drag.raf = requestAnimationFrame(paint);
    });
    function end(e) {
        if (!drag || (e && e.pointerId !== drag.id)) return;
        if (drag.raf) cancelAnimationFrame(drag.raf);
        drag.raf = 0; paint();
        drag.box.classList.remove('ipk-dragging');
        try { drag.head.releasePointerCapture(drag.id); } catch { }
        drag = null;
    }
    window.addEventListener('pointerup', end);
    window.addEventListener('pointercancel', end);
})();
