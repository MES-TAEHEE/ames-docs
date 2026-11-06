// Normalize before Blazor reads the input event so the display and preview use the same value.
document.addEventListener('input', event => {
    const input = event.target;
    if (event.isComposing || !input?.matches('input[data-shipment-qty]') || input.disabled || input.readOnly) return;
    const normalized = input.value.replace(/^0+(?=\d)/, '');
    if (input.value !== normalized) input.value = normalized;
}, true);
