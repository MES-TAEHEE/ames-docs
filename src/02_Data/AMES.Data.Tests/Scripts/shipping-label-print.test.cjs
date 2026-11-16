const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const js = path.resolve(__dirname, '../../../06_Web/AMES.Web/wwwroot/js');

test('batch print preserves every label and includes scoped label styles on request', () => {
    let html = '', printed = 0;
    const frameDocument = { open() {}, write(value) { html = value; }, close() {}, querySelectorAll() { return []; } };
    const frame = { style: {}, contentDocument: frameDocument, contentWindow: { focus() {}, print() { printed++; } } };
    const context = { window: {}, setTimeout(fn) { fn(); }, document: {
        querySelector() { return { outerHTML: '<div class="location-label-print-area"><div>CHILD-1</div><div>CHILD-2</div></div>' }; },
        querySelectorAll() { return [{ href: 'http://localhost/app.css' }, { href: 'http://localhost/AMES.Web.styles.css' }]; },
        getElementById() { return null; }, createElement() { return frame; }, body: { appendChild() {} }
    } };
    vm.runInNewContext(fs.readFileSync(path.join(js, 'label-print.js'), 'utf8'), context);
    assert.equal(context.window.amesPrintLabels('.location-label-print-area', '100mm 60mm', true), true);
    assert.match(html, /CHILD-1.*CHILD-2/);
    assert.match(html, /<html lang="en">/);
    assert.match(html, /AMES.Web.styles.css/);
    assert.match(html, /size: 100mm 60mm/);
    assert.match(html, /position: static !important/);
    assert.equal(printed, 1);
    context.window.amesPrintLabels('.location-label-print-area', '100mm 60mm');
    assert.doesNotMatch(html, /AMES.Web.styles.css/);
});

test('selected Zebra disappearing cannot redirect shipment labels to another printer', async () => {
    const calls = [];
    const context = { window: {}, location: { protocol: 'http:' }, console, fetch: async (url, options) => {
        calls.push(url);
        return { ok: true, text: async () => url.endsWith('/available')
            ? JSON.stringify({ printer: [{ uid: 'other', name: 'Other printer' }] })
            : JSON.stringify({ uid: 'other' }) };
    } };
    vm.runInNewContext(fs.readFileSync(path.join(js, 'zebra-print.js'), 'utf8'), context);
    const result = await context.window.amesZebraWrite('selected', '^XA^XZ', true);
    assert.equal(result.ok, false);
    assert.match(result.error, /Selected printer/);
    assert.equal(calls.length, 1);
    assert.ok(calls[0].endsWith('/available'));
});

test('Zebra sends one prepared label job to the selected device', async () => {
    let payload;
    const context = { window: {}, location: { protocol: 'http:' }, console, fetch: async (url, options) => {
        if (url.endsWith('/write')) payload = JSON.parse(options.body);
        return { ok: true, text: async () => url.endsWith('/available') ? JSON.stringify({ printer: [{ uid: 'selected' }] }) : '' };
    } };
    vm.runInNewContext(fs.readFileSync(path.join(js, 'zebra-print.js'), 'utf8'), context);
    assert.equal((await context.window.amesZebraWrite('selected', '^XA^PQ1^XZ', true)).ok, true);
    assert.equal(payload.device.uid, 'selected');
    assert.equal(payload.data, '^XA^PQ1^XZ');
});
