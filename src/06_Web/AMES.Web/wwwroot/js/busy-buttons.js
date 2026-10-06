// 처리 중 표시 — 저장류 색 버튼(Primary·Danger·Success·Warning)을 누른 뒤 서버 처리가 DELAY 안에 끝나지 않으면
// 그 버튼 자리에 스피너 + "처리 중…" 을 보인다. 끝은 서버가 알린다(AmesComponentBase → amesBusy.done):
// 동기 저장은 처리가 끝날 때까지 화면을 다시 그리지 못해 화면별 "…중" 문구로는 보이지 않기 때문이다.
// 버튼이 스스로 바뀌었으면(화면별 "적용 중…" 문구·비활성) 그 표시를 쓰고 여기서는 덮지 않는다.
window.amesBusy = (() => {
    const DELAY = 350, MAX = 30000, NARROW = 72;
    const LABEL = { ko: '처리 중…', en: 'Working…', es: 'Procesando…' };
    const SEL = 'button.rz-button.rz-primary, button.rz-button.rz-danger, button.rz-button.rz-success, button.rz-button.rz-warning';
    let pending = null;

    function clear() {
        if (!pending) return;
        clearTimeout(pending.show); clearTimeout(pending.max);
        pending.btn.classList.remove('ames-busy-btn');
        if (pending.w !== undefined) { pending.btn.style.width = pending.w; pending.btn.style.height = pending.h; }
        pending.btn.removeAttribute('data-busy-label');
        pending = null;
    }

    document.addEventListener('click', e => {
        const btn = e.target.closest && e.target.closest(SEL);
        if (!btn || btn.disabled || btn.closest('[data-no-busy]')) return;
        clear();
        const text = btn.innerText;
        const p = { btn };
        p.show = setTimeout(() => {
            if (pending !== p || !btn.isConnected || btn.disabled || btn.innerText !== text) return;
            const lang = (document.documentElement.lang || 'ko').slice(0, 2);
            btn.setAttribute('data-busy-label', btn.offsetWidth < NARROW ? '' : (LABEL[lang] || LABEL.en));
            // 내용을 숨기는 동안 버튼 크기가 바뀌지 않게 지금 크기로 고정한다
            p.w = btn.style.width; p.h = btn.style.height;
            btn.style.width = btn.offsetWidth + 'px'; btn.style.height = btn.offsetHeight + 'px';
            btn.classList.add('ames-busy-btn');
        }, DELAY);
        p.max = setTimeout(clear, MAX);
        pending = p;
    }, true);

    return { done: clear };
})();
