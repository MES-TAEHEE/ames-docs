// Normalize before Blazor reads the input event so the display and preview use the same value.
document.addEventListener('input', event => {
    const input = event.target;
    if (event.isComposing || !input?.matches('input[data-shipment-qty]') || input.disabled || input.readOnly) return;
    const normalized = input.value.replace(/^0+(?=\d)/, '');
    if (input.value !== normalized) input.value = normalized;
}, true);

// Tab / Shift+Tab 은 다음 칸이 아니라 같은 열의 아래 / 위 출하 수량 칸으로 옮긴다(값을 위에서 아래로 연달아 채울 때).
// 끝(첫·마지막 줄)에서는 기본 동작으로 넘긴다. 이동한 칸은 값 전체를 선택해 바로 덮어쓸 수 있다.
document.addEventListener('keydown', event => {
    const input = event.target;
    if (event.key !== 'Tab' || event.ctrlKey || event.altKey || event.metaKey || event.isComposing) return;
    if (!input?.matches?.('input[data-shipment-qty]')) return;
    const all = Array.from(document.querySelectorAll('input[data-shipment-qty]')).filter(i => !i.disabled && !i.readOnly && i.offsetParent !== null);
    const at = all.indexOf(input);
    const next = all[at + (event.shiftKey ? -1 : 1)];
    if (at < 0 || !next) return;
    event.preventDefault();
    next.focus();
    next.select?.();
    next.scrollIntoView?.({ block: 'nearest' });
}, true);
