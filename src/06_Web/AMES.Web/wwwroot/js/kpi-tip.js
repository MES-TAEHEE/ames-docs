// KPI 성과지표 도움말 툴팁 — KpiHelp 컴포넌트(ⓘ)의 data-tip 을 body 에 띄운 떠다니는 박스로 보인다.
// KPI 카드는 overflow:hidden 이라 카드 안에서 그리면 잘리므로 body 에 position:fixed 로 그린다.
// 지표 제목(.ames-kpi-l)이나 ⓘ 에 마우스를 올리거나(키보드는 ⓘ 에 포커스) 터치로 ⓘ 를 누르면 보이고, 포인터는 막지 않는다(화면을 가리지 않음).
(function () {
    if (window.__amesKpiTip) return;
    window.__amesKpiTip = true;

    var box = null, timer = 0, host = null;

    function ensure() {
        if (!box) {
            box = document.createElement('div');
            box.className = 'ames-kpi-tip';
            box.setAttribute('role', 'tooltip');
            document.body.appendChild(box);
        }
        return box;
    }

    function hide() {
        clearTimeout(timer);
        host = null;
        if (box) box.classList.remove('on');
    }

    function show(h, helpEl) {
        var text = helpEl.getAttribute('data-tip');
        if (!text) return;
        var b = ensure();
        b.textContent = text;
        b.style.left = '0px'; b.style.top = '0px';
        b.classList.add('on');
        var r = h.getBoundingClientRect(), w = b.offsetWidth, ht = b.offsetHeight, gap = 6;
        var left = Math.max(8, Math.min(r.left, window.innerWidth - w - 8));
        var top = r.bottom + gap;
        if (top + ht > window.innerHeight - 8) top = Math.max(8, r.top - ht - gap);
        b.style.left = left + 'px';
        b.style.top = top + 'px';
    }

    function resolve(t) {
        if (!t || !t.closest) return null;
        var h = t.closest('.ames-kpi-l, .ames-kpi-help');
        if (!h) return null;
        var helpEl = h.classList.contains('ames-kpi-help') ? h : h.querySelector('.ames-kpi-help');
        return helpEl ? { host: h, help: helpEl } : null;
    }

    document.addEventListener('mouseover', function (e) {
        var x = resolve(e.target);
        if (!x) return;
        if (host === x.host) return;
        hide();
        host = x.host;
        timer = setTimeout(function () { show(x.host, x.help); }, 120);
    });
    document.addEventListener('mouseout', function (e) {
        if (!host) return;
        var to = e.relatedTarget;
        if (to && host.contains(to)) return;
        hide();
    });
    document.addEventListener('focusin', function (e) {
        var x = resolve(e.target);
        if (x && x.help === e.target) { hide(); host = x.host; show(x.host, x.help); }
    });
    document.addEventListener('focusout', hide);
    document.addEventListener('click', function (e) {
        var x = resolve(e.target);
        if (x && x.help === e.target.closest('.ames-kpi-help')) {   // 터치: ⓘ 를 누르면 토글
            if (box && box.classList.contains('on') && host === x.host) hide();
            else { hide(); host = x.host; show(x.host, x.help); }
        } else hide();
    });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') hide(); });
    window.addEventListener('scroll', hide, true);
    window.addEventListener('resize', hide);
})();
