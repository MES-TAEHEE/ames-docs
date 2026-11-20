// 화면 이동 시 세션(인증 쿠키) 유지 핑 — 11-19 앱 전체 실시간 연결(D) 대응.
// 전에는 화면 이동마다 서버에 HTTP 요청이 가서 쿠키 슬라이딩 만료가 저절로 연장됐다. 이제 이동이 실시간 연결 안에서 끝나
// HTTP 요청이 없으므로, 주소가 바뀔 때 /keep-alive 를 보내 같은 효과를 낸다(1분에 한 번까지). 외부 개방 화면(/portal)은 제외.
// 만료된 세션이면 새로고침해 로그인으로 보낸다(만료 판정은 서버 쿠키 기준 — 마지막 핑 뒤 1분 넘게 지나 이동하면 반드시 확인됨).
(function () {
    'use strict';
    if (window.__amesNavKeepAlive) return;
    window.__amesNavKeepAlive = true;

    var last = Date.now(), MIN_GAP = 60000;

    function ping() {
        if (/^\/portal(\/|$)/i.test(location.pathname)) return;
        var now = Date.now();
        if (now - last < MIN_GAP) return;
        last = now;
        // 세션이 이미 만료됐으면(로그인 화면으로 돌려보내짐·401) 전체 새로고침 — 서버가 로그인 화면으로 보낸다.
        // 연결 안의 이동은 쿠키를 다시 확인하지 않으므로, 만료 뒤에도 화면을 계속 옮겨 다니지 않게 한다.
        try {
            fetch('keep-alive', { credentials: 'same-origin', cache: 'no-store' }).then(function (r) {
                if (r.status === 401 || r.status === 403 || (r.redirected && /\/Account\/(Login|AccessDenied)/i.test(r.url))) location.reload();
            }).catch(function () { });
        } catch (e) { }
    }

    ['pushState', 'replaceState'].forEach(function (name) {
        var orig = history[name];
        if (typeof orig !== 'function') return;
        history[name] = function () {
            var r = orig.apply(this, arguments);
            try { ping(); } catch (e) { }
            return r;
        };
    });
    window.addEventListener('popstate', ping);
})();
