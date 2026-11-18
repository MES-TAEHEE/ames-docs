// 움직일 수 있는 팝업 — 모든 팝업(.ames-modal 의 머리 .ames-modal-h, Radzen 대화상자의 제목줄 .rz-dialog-titlebar)을 잡고 끌어 옮긴다.
// 화면마다 따로 붙이지 않도록 전역에 한 번만 불러 쓴다(App.razor). 팝업을 닫았다 다시 열면 요소를 새로 그리므로 가운데로 돌아온다.
// 품번 BOM 미리보기(.ipk)는 item-peek.js 가 따로 끈다. 움직임은 transform 만 바꾸고 프레임마다 한 번만 그린다(requestAnimationFrame).
// 머리의 글자 위에서 누르면 끌기가 아니라 글자 선택이다 — 글자를 복사할 수 있게 하고, 글자가 없는 머리의 빈 자리를 잡아 끈다.
// 글자 칸 위인지는 머리 안의 글자 조각들의 화면 사각형으로 판정한다(item-peek.js 의 BOM 팝업도 같이 쓴다).
window.amesOverText = (root, x, y) => {
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    const range = document.createRange();
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
        if (!n.nodeValue || !n.nodeValue.trim()) continue;
        range.selectNodeContents(n);
        for (const r of range.getClientRects())
            if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) return true;
    }
    return false;
};

(() => {
    const HEAD = '.ames-modal:not(.ipk) > .ames-modal-h, .rz-dialog .rz-dialog-titlebar';
    let drag = null;

    function paint() {
        if (!drag) return;
        drag.raf = 0;
        drag.box.style.transform = `translate3d(${drag.x}px, ${drag.y}px, 0)`;
    }

    // 끌기를 팝업 밖(배경)에서 놓으면 브라우저가 배경 클릭으로 보내 팝업이 닫힌다 — 끈 직후 클릭 한 번은 버린다
    function swallowNextClick() {
        const stop = e => { e.stopPropagation(); e.preventDefault(); };
        window.addEventListener('click', stop, { capture: true, once: true });
        setTimeout(() => window.removeEventListener('click', stop, { capture: true }), 0);
    }

    window.addEventListener('pointerdown', e => {
        if (e.button !== 0) return;
        const head = e.target.closest ? e.target.closest(HEAD) : null;
        // 머리 안의 버튼·링크·입력은 끌기로 보지 않는다
        if (!head || e.target.closest('button, a, input, select, textarea, .rz-dialog-titlebar-close')) return;
        if (window.amesOverText(head, e.clientX, e.clientY)) return;   // 글자 위 — 선택·복사
        const box = head.closest('.ames-modal, .rz-dialog');
        if (!box) return;
        const m = /translate3d\((-?[\d.]+)px, (-?[\d.]+)px/.exec(box.style.transform || '');
        const r = box.getBoundingClientRect();
        drag = {
            box, head, id: e.pointerId, sx: e.clientX, sy: e.clientY, moved: false,
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
        if (Math.abs(dx) > 2 || Math.abs(dy) > 2) drag.moved = true;
        drag.x = drag.bx + dx; drag.y = drag.by + dy;
        if (!drag.raf) drag.raf = requestAnimationFrame(paint);
    });

    function end(e) {
        if (!drag || (e && e.pointerId !== drag.id)) return;
        if (drag.raf) cancelAnimationFrame(drag.raf);
        drag.raf = 0; paint();
        drag.box.classList.remove('ames-modal-dragging');
        try { drag.head.releasePointerCapture(drag.id); } catch { }
        if (drag.moved) swallowNextClick();
        drag = null;
    }
    window.addEventListener('pointerup', end);
    window.addEventListener('pointercancel', end);
})();

// 팝업 안에서 누른 채 끌어(글자 선택·입력칸 드래그 등) 배경에서 놓으면 브라우저가 "배경 클릭" 으로 보내 팝업이 닫힌다.
// 배경 클릭은 누른 곳도 배경일 때만 인정한다 — 누른 곳이 팝업 안이면 그 클릭은 버린다(.ames-modal 배경과 Radzen 대화상자 마스크 공통).
(() => {
    const BACKDROP = '.ames-modal-bg, .rz-dialog-mask, .rz-dialog-wrapper';
    let downTarget = null;
    window.addEventListener('mousedown', e => { downTarget = e.target; }, true);
    window.addEventListener('click', e => {
        const t = e.target;
        if (!t || !t.matches?.(BACKDROP)) return;
        if (downTarget && downTarget !== t) { e.stopImmediatePropagation(); e.preventDefault(); }
    }, true);
    window.addEventListener('click', () => { downTarget = null; });   // 눌림 기록은 클릭 한 번에만 쓴다(키보드·코드로 낸 클릭에 남지 않게)
})();
