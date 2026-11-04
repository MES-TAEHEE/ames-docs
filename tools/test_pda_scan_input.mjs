import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const script = readFileSync(new URL('../src/05_Pda/AMES.Pda/wwwroot/js/pda-scan.js', import.meta.url), 'utf8');

function scanner() {
    let now = 1000, nextTimer = 0, focused, capture, inert = false;
    const timers = new Map(), handlers = new Map(), scans = [];
    function element(tagName = 'INPUT', scanInput = false) {
        return {
            tagName, value: '', style: {}, classList: { add() {}, remove() {} },
            setAttribute() {}, removeAttribute() {}, focus() { focused = this; },
            matches: selector => scanInput && selector === '.pda-scan-input',
            closest(selector) { return this.matches(selector) ? this : null; }
        };
    }
    const document = {
        body: { contains: item => item === capture, appendChild: item => { capture = item; } },
        createElement: () => element(), querySelectorAll: () => [],
        querySelector: selector => selector === '.pda-root[inert]' && inert ? {} : null,
        addEventListener: (type, handler) => handlers.set(type, handler)
    };
    const context = {
        window: {}, document, performance: { now: () => now },
        setTimeout: (handler, delay) => { const id = ++nextTimer; timers.set(id, { handler, due: now + delay }); return id; },
        clearTimeout: id => timers.delete(id)
    };
    vm.runInNewContext(script, context);
    context.window.pdaScan.register({
        invokeMethodAsync(method, text) {
            assert.equal(method, 'OnHardwareScanAsync');
            scans.push(text);
            return Promise.resolve();
        }
    });
    function advance(ms) {
        const end = now + ms;
        while (true) {
            const next = [...timers].sort((a, b) => a[1].due - b[1].due)[0];
            if (!next || next[1].due > end) break;
            now = next[1].due;
            timers.delete(next[0]);
            next[1].handler();
        }
        now = end;
    }
    function dispatch(type, target, values = {}) {
        const event = {
            target, defaultPrevented: false, ...values,
            preventDefault() { this.defaultPrevented = true; }, stopImmediatePropagation() {}
        };
        handlers.get(type)(event);
        return event;
    }
    function commit(text, suffix) {
        capture.value = text;
        dispatch('input', capture);
        if (suffix) return dispatch('keydown', capture, { key: suffix });
    }
    return {
        scans, advance, dispatch, commit, element,
        get capture() { return capture; }, get focused() { return focused; },
        set inert(value) { inert = value; }, unregister: () => context.window.pdaScan.unregister()
    };
}

// Key-event wedges and IME/Commit Text wedges share the same delivery callback.
{
    const input = scanner();
    for (const key of 'BOX-KEYS-01') {
        const event = input.dispatch('keydown', input.capture, { key });
        if (!event.defaultPrevented) input.capture.value += key;
        input.dispatch('input', input.capture);
        input.advance(10);
    }
    assert.equal(input.dispatch('keydown', input.capture, { key: 'Enter' }).defaultPrevented, true);
    input.advance(1000);
    assert.deepEqual(input.scans, ['BOX-KEYS-01']);
    assert.equal(input.capture.value, '');
}
for (const suffix of ['Enter', 'Tab']) {
    const input = scanner();
    assert.equal(input.commit('BOX-COMMIT-01', suffix).defaultPrevented, true);
    assert.deepEqual(input.scans, ['BOX-COMMIT-01']);
    assert.equal(input.capture.value, '');
    input.advance(1000);
    assert.equal(input.scans.length, 1);
}
{
    const input = scanner();
    input.commit('\u0002BOX-NO-SUFFIX\u0003\r\n');
    input.advance(450);
    assert.deepEqual(input.scans, ['BOX-NO-SUFFIX']);
}
{
    const input = scanner();
    input.commit('BOX-DUPLICATE', 'Enter');
    input.commit('BOX-DUPLICATE', 'Enter');
    assert.equal(input.scans.length, 1);
    input.advance(1201);
    input.commit('BOX-DUPLICATE', 'Enter');
    assert.equal(input.scans.length, 2);
}
for (const tagName of ['INPUT', 'TEXTAREA', 'SELECT']) {
    const input = scanner();
    const field = input.element(tagName);
    field.value = 'manual entry';
    for (const key of '1234') input.dispatch('keydown', field, { key });
    assert.equal(input.dispatch('keydown', field, { key: 'Enter' }).defaultPrevented, false);
    input.dispatch('input', field);
    input.advance(1000);
    assert.deepEqual(input.scans, []);
    assert.equal(field.value, 'manual entry');
}
{
    const input = scanner();
    input.inert = true;
    input.commit('BOX-SESSION-CHECK', 'Enter');
    input.advance(1000);
    assert.deepEqual(input.scans, []);
}
{
    const input = scanner();
    assert.equal(input.dispatch('pointerdown', input.element('INPUT', true)).defaultPrevented, true);
    assert.equal(input.focused, input.capture);
    input.commit('BOX-UNREGISTERED');
    input.unregister();
    input.advance(1000);
    assert.deepEqual(input.scans, []);
}
console.log('PASS: 11 common wedge input cases; Enter/Tab, IME text, normalization, duplicates, manual fields, session guard and teardown.');
