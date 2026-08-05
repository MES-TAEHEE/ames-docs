// AMES 테마 토글 — data-theme("light"|없음=dark)를 <html>에 적용.
// 영속: localStorage(무플래시용) + 쿠키(서버가 매 응답에 data-theme 렌더 → 향상된 네비게이션에도 유지).
(function () {
    const KEY = 'ames.theme';
    const root = document.documentElement;
    function readCookie() {
        const m = document.cookie.match(/(?:^|;\s*)ames\.theme=(light|dark)/);
        return m ? m[1] : null;
    }
    function writeCookie(t) {
        document.cookie = 'ames.theme=' + t + ';path=/;max-age=31536000;samesite=lax';
    }
    function apply(t) {
        if (t === 'light') root.setAttribute('data-theme', 'light');
        else root.removeAttribute('data-theme');
    }
    window.amesTheme = {
        get() { try { return localStorage.getItem(KEY) || readCookie() || 'dark'; } catch (e) { return readCookie() || 'dark'; } },
        set(t) { try { localStorage.setItem(KEY, t); } catch (e) { } writeCookie(t); apply(t); return t; },
        toggle() { return this.set(this.get() === 'light' ? 'dark' : 'light'); },
        init() { const t = this.get(); writeCookie(t); apply(t); }   // 쿠키 동기화(서버 렌더용)
    };
    window.amesTheme.init();
})();
