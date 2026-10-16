window.locationMapScroll = (() => {
    const update = (scroll) => {
        const shell = scroll?.closest('.location-map-scroll-shell');
        if (!shell) return;

        const remaining = {
            left: scroll.scrollLeft > 1,
            right: scroll.scrollLeft + scroll.clientWidth < scroll.scrollWidth - 1,
            up: scroll.scrollTop > 1,
            down: scroll.scrollTop + scroll.clientHeight < scroll.scrollHeight - 1
        };

        for (const [direction, visible] of Object.entries(remaining)) {
            shell.querySelector(`[data-scroll-dir="${direction}"]`)?.toggleAttribute('hidden', !visible);
        }
    };

    const init = (id) => {
        const scroll = document.getElementById(id);
        if (!scroll) return;
        if (!scroll.dataset.arrowScroll) {
            scroll.dataset.arrowScroll = 'true';
            scroll.addEventListener('scroll', () => update(scroll), { passive: true });
        }
        requestAnimationFrame(() => update(scroll));
    };

    const move = (id, direction) => {
        const scroll = document.getElementById(id);
        if (!scroll) return;
        const x = Math.max(120, scroll.clientWidth * .75);
        const y = Math.max(120, scroll.clientHeight * .75);
        scroll.scrollBy({
            left: direction === 'left' ? -x : direction === 'right' ? x : 0,
            top: direction === 'up' ? -y : direction === 'down' ? y : 0,
            behavior: 'smooth'
        });
    };

    window.addEventListener('resize', () => {
        document.querySelectorAll('.location-coordinate-scroll[data-arrow-scroll]').forEach(update);
    });

    return { init, move };
})();
