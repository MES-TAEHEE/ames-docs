const watches = new Map();

export function observe(id, receiver, method) {
    const element = document.getElementById(id);
    if (!element) { dispose(id); return; }
    let watch = watches.get(id);
    if (watch?.element === element) {
        if (!watch.pending) watch.observer.observe(element);
        return;
    }
    dispose(id);
    let root = element.parentElement;
    while (root && !/(auto|scroll)/.test(getComputedStyle(root).overflowY)) root = root.parentElement;
    watch = { element, pending: false, observer: null };
    watch.observer = new IntersectionObserver(async entries => {
        if (watch.pending || !entries.some(e => e.isIntersecting)) return;
        watch.pending = true;
        watch.observer.unobserve(element);
        try {
            if (await receiver.invokeMethodAsync(method) && element.isConnected) watch.observer.observe(element);
        } catch { /* The page may have been disposed during a request. */ }
        finally { watch.pending = false; }
    }, { root, rootMargin: '120px', threshold: 0 });
    watches.set(id, watch);
    watch.observer.observe(element);
}

export function dispose(id) {
    watches.get(id)?.observer.disconnect();
    watches.delete(id);
}
