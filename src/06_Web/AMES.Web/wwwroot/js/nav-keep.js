// 언어 전환(쿠키 설정 + 새로고침) 때 좌측 메뉴의 열린 섹션·하위 그룹과 메뉴·본문 스크롤을 그대로 이어 준다.
// save() 는 새로고침 직전(CultureSwitcher), take() 는 새로고침 뒤 메뉴 첫 렌더에서 한 번 — 같은 주소일 때만 돌려준다.
// 평소 로그인·새 화면 열기에는 쓰지 않는다(그때 메뉴는 열린 화면의 섹션만 연다).
window.amesNavKeep = {
    key: 'ames.nav.keep',
    save: function () {
        try {
            var nav = document.querySelector('.ames-nav');
            var main = document.querySelector('.ames-main');
            var state = {
                path: location.pathname + location.search,
                sec: Array.prototype.map.call(document.querySelectorAll('[data-nav-sec].open'), function (e) { return e.getAttribute('data-nav-sec'); }),
                sub: Array.prototype.map.call(document.querySelectorAll('[data-nav-sub].open'), function (e) { return e.getAttribute('data-nav-sub'); }),
                nav: nav ? Math.round(nav.scrollTop) : 0,
                main: main ? Math.round(main.scrollTop) : 0
            };
            sessionStorage.setItem(this.key, JSON.stringify(state));
        } catch (e) { }
    },
    take: function () {
        try {
            var v = sessionStorage.getItem(this.key);
            if (!v) return null;
            sessionStorage.removeItem(this.key);
            var s = JSON.parse(v);
            return s.path === location.pathname + location.search ? v : null;
        } catch (e) { return null; }
    },
    // 본문은 데이터를 읽은 뒤에야 길어지므로 몇 번 나눠 맞춘다 — 이미 그 위치보다 아래로 내려가 있으면(사용자가 움직였으면) 건드리지 않는다
    scroll: function (navTop, mainTop) {
        var apply = function () {
            var nav = document.querySelector('.ames-nav');
            if (nav && nav.scrollTop < navTop) nav.scrollTop = navTop;
            var main = document.querySelector('.ames-main');
            if (main && main.scrollTop < mainTop) main.scrollTop = mainTop;
        };
        apply();
        [150, 500, 1200, 2500].forEach(function (t) { setTimeout(apply, t); });
    }
};
