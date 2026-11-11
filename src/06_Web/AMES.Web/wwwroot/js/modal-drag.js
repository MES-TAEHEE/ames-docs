// 움직일 수 있는 팝업 — 클래스 ames-movable 을 단 .ames-modal 은 머리(.ames-modal-h)를 잡고 끌어 옮긴다.
// 화면마다 따로 붙이지 않도록 전역에 한 번만 불러 쓴다(App.razor). 팝업을 닫았다 다시 열면 요소를 새로 그리므로 가운데로 돌아온다.
// 움직임은 transform 만 바꾼다 — 레이아웃을 다시 계산하지 않고, 프레임마다 한 번만 그린다(requestAnimationFrame).
(() => {
    let drag = null;

    function paint() {
        if (!drag) return;
        drag.raf = 0;
        drag.box.style.transform = `translate3d(${drag.x}px, ${drag.y}px, 0)`;
    }

    window.addEventListener('pointerdown', e => {
        if (e.button !== 0) return;
        const head = e.target.closest ? e.target.closest('.ames-movable > .ames-modal-h') : null;
        // 머리 안의 버튼·링크·입력은 끌기로 보지 않는다
        if (!head || e.target.closest('button, a, input, select, textarea')) return;
        const box = head.parentElement;
        const m = /translate3d\((-?[\d.]+)px, (-?[\d.]+)px/.exec(box.style.transform || '');
        const r = box.getBoundingClientRect();
        drag = {
            box, head, id: e.pointerId, sx: e.clientX, sy: e.clientY,
            bx: m ? +m[1] : 0, by: m ? +m[2] : 0, x: 0, y: 0, raf: 0,
            // 머리가 화면 밖으로 완전히 나가 다시 잡을 수 없게 되지 않도록 이동 범위를 둔다
            minX: -r.left - r.width + 80, maxX: window.innerWidth - r.left - 80,
            minY: -r.top, maxY: window.innerHeight - r.top - 40
        };
        drag.x = drag.bx; drag.y = drag.by;
        try { head.setPointerCapture(e.pointerId); } catch { }
        box.classList.add('ames-modal-dragging');
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
        drag.box.classList.remove('ames-modal-dragging');
        try { drag.head.releasePointerCapture(drag.id); } catch { }
        drag = null;
    }
    window.addEventListener('pointerup', end);
    window.addEventListener('pointercancel', end);
})();
