// 열 너비 조정: data-col-resize="이름" 이 붙은 표의 머리글 칸 오른쪽 경계를 끌어 폭을 바꾼다.
// 폭은 이름별로 브라우저에 저장되어 다시 열어도 유지된다. Blazor 가 표를 다시 그려도
// MutationObserver 로 새 머리글에 손잡이를 다시 붙인다.
(function () {
    const KEY = 'ames-col-w:';
    const MIN_W = 48;

    function load(name) {
        try { return JSON.parse(localStorage.getItem(KEY + name) || '{}'); } catch (_) { return {}; }
    }
    function save(name, widths) {
        try { localStorage.setItem(KEY + name, JSON.stringify(widths)); } catch (_) { }
    }

    function applySaved(table, name) {
        const saved = load(name);
        const ths = table.querySelectorAll(':scope > thead > tr:first-child > th');
        ths.forEach((th, i) => {
            if (saved[i]) th.style.width = saved[i] + 'px';
        });
    }

    function attach(table) {
        if (table.dataset.colResizeReady === '1') return;
        const name = table.dataset.colResize;
        if (!name) return;
        table.dataset.colResizeReady = '1';
        applySaved(table, name);

        const ths = table.querySelectorAll(':scope > thead > tr:first-child > th');
        ths.forEach((th, i) => {
            th.style.position = 'relative';
            const grip = document.createElement('div');
            grip.className = 'ames-col-grip';
            grip.title = '드래그해서 열 너비 조정';
            th.appendChild(grip);

            grip.addEventListener('mousedown', (e) => {
                e.preventDefault();
                e.stopPropagation();
                const startX = e.clientX;
                const startW = th.getBoundingClientRect().width;
                document.body.style.cursor = 'col-resize';
                grip.classList.add('active');

                const move = (ev) => {
                    const w = Math.max(MIN_W, startW + (ev.clientX - startX));
                    th.style.width = Math.round(w) + 'px';
                };
                const up = () => {
                    document.removeEventListener('mousemove', move);
                    document.removeEventListener('mouseup', up);
                    document.body.style.cursor = '';
                    grip.classList.remove('active');
                    const widths = {};
                    table.querySelectorAll(':scope > thead > tr:first-child > th').forEach((h, k) => {
                        const cur = parseFloat(h.style.width);
                        if (!isNaN(cur)) widths[k] = Math.round(cur);
                    });
                    save(name, widths);
                };
                document.addEventListener('mousemove', move);
                document.addEventListener('mouseup', up);
            });
            // 손잡이 더블클릭: 저장된 폭을 지우고 기본 폭으로 되돌린다.
            grip.addEventListener('dblclick', (e) => {
                e.stopPropagation();
                th.style.width = '';
                const widths = load(name);
                delete widths[i];
                save(name, widths);
            });
        });
    }

    function scan(root) {
        (root || document).querySelectorAll('table[data-col-resize]').forEach(attach);
    }

    const obs = new MutationObserver(() => scan(document));
    obs.observe(document.documentElement, { childList: true, subtree: true });

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => scan(document));
    else scan(document);
})();
