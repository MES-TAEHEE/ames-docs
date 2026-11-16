const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.resolve(__dirname, '../../../06_Web/AMES.Web/wwwroot/js/modal-keys.js'), 'utf8');

function modal(z = 1000, parent = null, disabled = false) {
    const overlay = { parentElement: parent, zIndex: String(z), matches: s => s === '.ames-modal-bg' };
    const m = { parentElement: overlay, offsetParent: {}, getClientRects: () => [{}] };
    const close = { offsetParent: {}, getClientRects: () => [{}], disabled, clicks: 0,
        closest: () => m, click() { this.clicks++; } };
    const cancel = { ...close, innerText: 'Cancel', clicks: 0, querySelector: () => null };
    m.querySelectorAll = s => s === '[data-portal-close]' ? [close] : [cancel, close];
    return { modal: m, close, cancel, overlay };
}

function page({ modals = [], popups = [], dialogs = [], parent = null, pathname = '/portal/deliveries' } = {}) {
    const handlers = {};
    const document = {
        addEventListener(type, handler) { (handlers[type] ||= []).push(handler); },
        querySelectorAll(s) {
            if (s === '.ames-modal') return modals.map(m => m.modal);
            if (s === '.rz-dialog') return dialogs;
            if (s === '.rz-popup, .rz-datepicker-popup-container') return popups;
            return [];
        }
    };
    const window = { location: { pathname } };
    window.parent = parent || window;
    vm.runInNewContext(script, { document, window, getComputedStyle: e => ({ zIndex: e.zIndex, display: 'block' }) });
    return function escape(extra = {}) {
        const e = { key: 'Escape', defaultPrevented: false, isComposing: false,
            preventDefault() { this.defaultPrevented = true; }, stopPropagation() {}, ...extra };
        handlers.keydown.forEach(h => h(e));
        return e;
    };
}

test('Portal closes only the highest visible layer, even if it precedes the background in DOM order', () => {
    const printer = modal(1500), background = modal(1000);
    page({ modals: [printer, background] })();
    assert.equal(printer.close.clicks, 1);
    assert.equal(background.close.clicks, 0);
    assert.equal(printer.cancel.clicks, 0);
});

test('Nested stacking contexts respect the outer preview rather than a child behind it', () => {
    const background = modal(1000), printer = modal(1500, background.overlay), preview = modal(1200);
    page({ modals: [background, printer, preview] })();
    assert.equal(preview.close.clicks, 1);
    assert.equal(printer.close.clicks, 0);
});

test('Disabled top close does not close its background or execute a Cancel business action', () => {
    const background = modal(), busy = modal(1500, null, true);
    page({ modals: [background, busy] })();
    assert.equal(background.close.clicks + busy.close.clicks + busy.cancel.clicks, 0);
});

test('An open dropdown or calendar gets Escape before the surrounding modal', () => {
    const m = modal();
    const popup = { offsetParent: {}, getClientRects: () => [{}] };
    const e = page({ modals: [m], popups: [popup] })();
    assert.equal(m.close.clicks, 0);
    assert.equal(e.defaultPrevented, false);
});

test('Radzen confirmation owns Escape without dismissing the underlying document', () => {
    const m = modal();
    const dialog = { offsetParent: {}, getClientRects: () => [{}] };
    page({ modals: [m], dialogs: [dialog] })();
    assert.equal(m.close.clicks, 0);
});

test('A focused print iframe forwards Escape to its same-origin parent', () => {
    const events = [];
    const parent = { document: { dispatchEvent(e) { events.push(e); } },
        KeyboardEvent: class { constructor(type, options) { this.type = type; Object.assign(this, options); } } };
    const e = page({ parent, pathname: '/portal/box-labels' })();
    assert.equal(events.length, 1);
    assert.equal(events[0].key, 'Escape');
    assert.equal(e.defaultPrevented, true);
});

test('A printer popup inside an iframe closes locally without also closing the parent preview', () => {
    const m = modal(1500);
    const parent = { document: { dispatchEvent() { throw new Error('Parent must remain open'); } } };
    page({ parent, modals: [m], pathname: '/portal/box-labels' })();
    assert.equal(m.close.clicks, 1);
});

test('Cross-origin parent access fails safely; composing or already-handled Escape does nothing', () => {
    const parent = {};
    Object.defineProperty(parent, 'document', { get() { throw new Error('cross-origin'); } });
    assert.doesNotThrow(() => page({ parent })());
    const m = modal();
    const esc = page({ modals: [m] });
    esc({ isComposing: true }); esc({ defaultPrevented: true });
    assert.equal(m.close.clicks, 0);
});

test('Non-Portal pages retain their previous last-modal behavior', () => {
    const first = modal(1500), last = modal(1000);
    page({ modals: [first, last], pathname: '/md/items' })();
    assert.equal(first.close.clicks, 0);
    assert.equal(last.close.clicks, 1);
});
