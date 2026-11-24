const maps = new WeakMap();

export function rectangle(a, b) {
    const clamp = n => Math.round(Math.max(0, Math.min(100, n)) * 10000) / 10000;
    const x = Math.min(clamp(a.x), clamp(b.x));
    const y = Math.min(clamp(a.y), clamp(b.y));
    return { x, y, width: clamp(Math.max(clamp(a.x), clamp(b.x)) - x), height: clamp(Math.max(clamp(a.y), clamp(b.y)) - y) };
}

export function initialize(root, viewport, canvas, dotnet) {
    dispose(root);
    const image = canvas.querySelector('img');
    const draft = canvas.querySelector('.factory-map-draft');
    const controller = new AbortController();
    const options = { signal: controller.signal };
    const state = { root, viewport, canvas, image, draft, dotnet, controller, factor: 1, editing: false, modal: false, start: null, ready: false };
    maps.set(root, state);
    const invoke = (name, ...args) => dotnet.invokeMethodAsync(name, ...args).catch(() => {});
    const ready = () => {
        state.ready = image.naturalWidth > 0;
        if (state.ready) resize(state, false);
        invoke('MapReady', state.ready);
    };
    image.addEventListener('load', ready, options);
    image.addEventListener('error', () => invoke('MapReady', false), options);
    if (image.complete) ready();
    const point = e => {
        const bounds = canvas.getBoundingClientRect();
        return { x: (e.clientX - bounds.left) * 100 / bounds.width, y: (e.clientY - bounds.top) * 100 / bounds.height };
    };
    canvas.addEventListener('pointerdown', e => {
        if (!state.editing || !state.ready || e.button !== 0 || e.target.closest('button')) return;
        e.preventDefault();
        canvas.setPointerCapture(e.pointerId);
        state.start = point(e);
        draft.hidden = false;
        Object.assign(draft.style, { left: `${state.start.x}%`, top: `${state.start.y}%`, width: '0', height: '0' });
    }, options);
    canvas.addEventListener('pointermove', e => {
        if (!state.start) return;
        const r = rectangle(state.start, point(e));
        Object.assign(draft.style, { left: `${r.x}%`, top: `${r.y}%`, width: `${r.width}%`, height: `${r.height}%` });
    }, options);
    canvas.addEventListener('pointerup', e => {
        if (!state.start) return;
        const r = rectangle(state.start, point(e));
        state.start = null;
        draft.hidden = true;
        if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
        if (r.width >= .25 && r.height >= .25) {
            state.editing = false; // Ignore the next drag while the server opens the form.
            invoke('RectangleDrawn', r.x, r.y, r.width, r.height);
        }
    }, options);
    canvas.addEventListener('pointercancel', () => { state.start = null; draft.hidden = true; }, options);
    root.addEventListener('keydown', e => {
        if (e.key === 'Escape') { state.start = null; draft.hidden = true; invoke('CloseOverlay'); }
    }, options);
    document.addEventListener('fullscreenchange', () => {
        if (!document.fullscreenElement) root.classList.remove('factory-map-expanded');
    }, options);
    state.observer = new ResizeObserver(() => resize(state, true));
    state.observer.observe(viewport);
}

function resize(s, preserve) {
    if (!s.ready || !s.viewport.clientWidth || !s.viewport.clientHeight) return;
    const { viewport: v, canvas: c, image } = s;
    const x = c.clientWidth > v.clientWidth ? (v.scrollLeft + v.clientWidth / 2) / c.clientWidth : .5;
    const y = c.clientHeight ? (v.scrollTop + v.clientHeight / 2 - 12) / c.clientHeight : .5;
    const scale = Math.min((v.clientWidth - 32) / image.naturalWidth, (v.clientHeight - 32) / image.naturalHeight) * s.factor;
    c.style.width = `${image.naturalWidth * scale}px`;
    c.style.height = `${image.naturalHeight * scale}px`;
    if (preserve) { v.scrollLeft = Math.max(0, x * c.clientWidth - v.clientWidth / 2); v.scrollTop = Math.max(0, 12 + y * c.clientHeight - v.clientHeight / 2); }
    else { v.scrollLeft = 0; v.scrollTop = 0; }
}

export function setState(root, editing, modal) { const s = maps.get(root); if (s) { s.editing = editing; s.modal = modal; } }
export function zoom(root, factor) { const s = maps.get(root); if (s) { s.factor = Math.max(1, Math.min(12, s.factor * factor)); resize(s, true); } }
export function fit(root) { const s = maps.get(root); if (s) { s.factor = 1; resize(s, false); } }
export async function fullScreen(root) {
    if (root.classList.contains('factory-map-expanded')) { await document.exitFullscreen(); return; }
    // Keep global Radzen dropdown popups inside the fullscreen document's top layer.
    root.classList.add('factory-map-expanded');
    try { await document.documentElement.requestFullscreen(); }
    catch { root.classList.remove('factory-map-expanded'); }
}
export function dispose(root) {
    const s = maps.get(root);
    if (!s) return;
    if (root.classList.contains('factory-map-expanded') && document.fullscreenElement) document.exitFullscreen().catch(() => {});
    s.controller.abort(); s.observer.disconnect(); maps.delete(root);
}
