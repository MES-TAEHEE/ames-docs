// 팝업 ESC 닫기 · 검색창 지우기(X) — 화면마다 손대지 않고 공통으로 붙인다.
//  · ESC: 맨 위에 열린 .ames-modal 의 닫기·취소 버튼을 대신 누른다. 화면의 닫기 처리(미저장 확인 등)를 그대로 타게
//    상태를 직접 바꾸지 않는다. 드롭다운·달력 팝업이 열려 있으면 그것이 먼저 닫히도록 건너뛴다.
//    Radzen DialogService 창은 Radzen 이 스스로 ESC 로 닫는다.
//  · X: 팝업 밖의 한 줄 입력칸(검색·필터)에 값이 있으면 오른쪽 끝에 X 를 그리고, 그 자리를 누르면 값을 지운다.
//    입력칸에 요소를 끼우면 Blazor 의 화면 갱신과 어긋나므로 data 속성 + 배경 그림만 쓰고,
//    지운 뒤 input·change·Enter 이벤트를 보내 @bind·Change·Enter 조회가 그대로 돈다.
(() => {
    const CLOSE_TEXT = ['취소', '닫기', 'cancel', 'close', 'cancelar', 'cerrar'];
    const X_ZONE = 24;

    function visible(el) { return !!(el && el.offsetParent !== null && el.getClientRects().length); }

    function popupOpen() {
        return Array.from(document.querySelectorAll('.rz-popup, .rz-datepicker-popup-container'))
            .some(p => visible(p) && getComputedStyle(p).display !== 'none');
    }

    function closeButton(modal) {
        const explicit = Array.from(modal.querySelectorAll('[data-portal-close]'))
            .filter(b => b.closest('.ames-modal') === modal);
        if (explicit.length) return explicit.find(b => !b.disabled && visible(b)) || null;
        const btns = Array.from(modal.querySelectorAll('button')).filter(b => !b.disabled && visible(b));
        const byIcon = btns.filter(b => {
            const i = b.querySelector('.rzi, .material-icons, .notranslate');
            return i && i.textContent.trim() === 'close';
        });
        if (byIcon.length) return byIcon[byIcon.length - 1];
        const byText = btns.filter(b => CLOSE_TEXT.includes(b.innerText.trim().toLowerCase()));
        return byText.length ? byText[byText.length - 1] : null;
    }

    function modalLayer(modal) {
        const layers = [];
        for (let el = modal.parentElement; el; el = el.parentElement) {
            if (el.matches('.ames-modal-bg')) layers.unshift(parseInt(getComputedStyle(el).zIndex, 10) || 0);
        }
        return layers;
    }

    function topModal(modals) {
        return modals.reduce((top, modal) => {
            const a = modalLayer(top), b = modalLayer(modal);
            for (let i = 0; i < Math.max(a.length, b.length); i++) {
                if ((a[i] || 0) !== (b[i] || 0)) return (b[i] || 0) > (a[i] || 0) ? modal : top;
            }
            return modal;
        });
    }

    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape' || e.defaultPrevented || e.isComposing) return;
        if (popupOpen()) return;
        const path = window.location.pathname.toLowerCase();
        const portal = path === '/portal' || path.startsWith('/portal/');
        // Radzen handles its own dialog; do not also dismiss the page beneath it.
        if (portal && Array.from(document.querySelectorAll('.rz-dialog')).some(visible)) return;
        const modals = Array.from(document.querySelectorAll('.ames-modal')).filter(visible);
        if (!modals.length) {
            // Keyboard events inside an embedded print preview do not bubble to its parent.
            if (portal && window.parent !== window) {
                try {
                    window.parent.document.dispatchEvent(new window.parent.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
                    e.preventDefault();
                    e.stopPropagation();
                } catch { /* Cross-origin frames cannot dismiss the surrounding page. */ }
            }
            return;
        }
        const btn = closeButton(portal ? topModal(modals) : modals[modals.length - 1]);
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();
        btn.click();
    });

    function clearable(el) {
        return el && el.tagName === 'INPUT' && (el.type === 'text' || el.type === 'search')
            && !el.readOnly && !el.disabled && !el.closest('.ames-modal, [data-no-clear]')
            && !el.closest('.rz-dropdown, .rz-datepicker, .rz-numeric, .rz-autocomplete');
    }

    function mark(el) {
        if (!clearable(el)) return;
        if (el.value) el.setAttribute('data-ames-x', '');
        else el.removeAttribute('data-ames-x');
    }

    function inZone(el, e) {
        const r = el.getBoundingClientRect();
        return e.clientX >= r.right - X_ZONE && e.clientX <= r.right;
    }

    document.addEventListener('input', e => mark(e.target), true);
    document.addEventListener('change', e => mark(e.target), true);
    document.addEventListener('focusin', e => mark(e.target), true);
    document.addEventListener('mouseover', e => mark(e.target), true);

    document.addEventListener('mousemove', e => {
        const el = e.target;
        if (!el.hasAttribute || !el.hasAttribute('data-ames-x')) return;
        el.style.cursor = inZone(el, e) ? 'pointer' : '';
    }, true);

    document.addEventListener('mousedown', e => {
        const el = e.target;
        if (e.button !== 0 || !clearable(el) || !el.value || !inZone(el, e)) return;
        e.preventDefault();
        el.value = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
        // 검색칸은 Enter 로 조회하는 화면이 많아, 지운 뒤 Enter 를 보내 바로 다시 조회되게 한다
        const enter = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true };
        el.dispatchEvent(new KeyboardEvent('keydown', enter));
        el.dispatchEvent(new KeyboardEvent('keyup', enter));
        el.removeAttribute('data-ames-x');
        el.style.cursor = '';
        el.focus();
    }, true);
})();
