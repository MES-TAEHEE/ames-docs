// 좌측 메뉴 클릭 시 우측 콘텐츠 로딩 표시 (상단 진행바 + 오버레이)
// 정적 SSR 레이아웃 + 향상된 탐색(enhanced navigation) 환경 대응.
(function () {
    'use strict';
    var bar, active = false, safety;

    function ensureBar() {
        if (bar && document.body.contains(bar)) return bar;
        bar = document.createElement('div');
        bar.id = 'ames-nav-progress';
        document.body.appendChild(bar);
        return bar;
    }

    function start() {
        if (active) return;
        ensureBar();
        active = true;
        document.documentElement.classList.add('ames-navigating');
        bar.classList.remove('done', 'active');
        void bar.offsetWidth;               // 트랜지션 재시작
        bar.classList.add('active');
        clearTimeout(safety);
        safety = setTimeout(finish, 10000); // 안전장치: 종료 신호 누락 대비
    }

    function finish() {
        if (!active) return;
        active = false;
        clearTimeout(safety);
        document.documentElement.classList.remove('ames-navigating');
        bar.classList.add('done');
        setTimeout(function () { bar.classList.remove('active', 'done'); }, 400);
    }

    // 1) 좌측 메뉴(.ames-side) 내부 링크 클릭 → 즉시 시작 (서버 왕복 이전)
    document.addEventListener('click', function (e) {
        var a = e.target.closest ? e.target.closest('a[href]') : null;
        if (!a || !a.closest('.ames-side')) return;
        var href = a.getAttribute('href');
        if (!href || href.charAt(0) === '#' || a.target === '_blank') return;
        var dest;
        try { dest = new URL(href, location.href); } catch (err) { return; }
        if (dest.origin !== location.origin) return;
        if (dest.pathname === location.pathname && dest.search === location.search) return; // 동일 화면
        start();
    }, true);

    // 2) 향상된 탐색 로드 완료 → 종료 (Blazor 준비 후 등록)
    (function hookBlazor() {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', finish);
        } else {
            setTimeout(hookBlazor, 100);
        }
    })();

    // 3) 폴백: 전체 페이지 네비게이션(비-향상 탐색) 시에도 정리
    window.addEventListener('pagehide', finish);
})();
