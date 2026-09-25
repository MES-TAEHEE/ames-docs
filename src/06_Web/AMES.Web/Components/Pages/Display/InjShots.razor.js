// 공장 디스플레이(InjShots) 전용 모듈 — 화면 크기 전달, 전체 화면 전환, 무인 복구.
let dotnet = null, resizeTimer = 0, watchdog = 0, idleTimer = 0, gone = 0;

function report() {
    const g = document.querySelector('.dsp .ds-grid');
    const bar = document.querySelector('.dsp .ds-bar');
    const w = g ? g.clientWidth : innerWidth;
    const h = g ? g.clientHeight : innerHeight - (bar ? bar.offsetHeight : 0);
    dotnet?.invokeMethodAsync('OnResize', w, h).catch(() => { });
}
function onResize() { clearTimeout(resizeTimer); resizeTimer = setTimeout(report, 200); }
function onMove() {
    document.querySelector('.dsp')?.classList.remove('ds-idle');
    clearTimeout(idleTimer);
    idleTimer = setTimeout(() => document.querySelector('.dsp')?.classList.add('ds-idle'), 3000);
}

export function start(ref) {
    dotnet = ref;
    addEventListener('resize', onResize);
    addEventListener('mousemove', onMove);
    onMove();
    report();
    // 무인 디스플레이: 서버 재시작·네트워크 단절로 재연결 화면이 20초 넘게 떠 있으면 페이지를 다시 연다
    watchdog = setInterval(() => {
        const m = document.getElementById('components-reconnect-modal');
        const shown = !!m && m.isConnected && getComputedStyle(m).display !== 'none' && getComputedStyle(m).visibility !== 'hidden';
        gone = shown ? gone + 5 : 0;
        if (gone >= 20) location.reload();
    }, 5000);
}

export function stop() {
    removeEventListener('resize', onResize);
    removeEventListener('mousemove', onMove);
    clearInterval(watchdog); clearTimeout(resizeTimer); clearTimeout(idleTimer);
    document.querySelector('.dsp')?.classList.remove('ds-idle');
    dotnet = null;
}

// 브라우저 정책상 사용자 조작(클릭·더블클릭)이 있어야 전환된다
export function toggleFullscreen() {
    if (document.fullscreenElement) document.exitFullscreen();
    else document.documentElement.requestFullscreen?.().catch(() => { });
}
