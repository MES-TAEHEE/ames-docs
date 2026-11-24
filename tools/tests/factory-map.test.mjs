import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const source = readFileSync(new URL('../../src/06_Web/AMES.Web/wwwroot/js/fg-factory-map.js', import.meta.url), 'utf8');
const map = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

class Element {
    handlers = new Map();
    style = {};
    captured = new Set();
    classes = new Set();
    classList = { add: value => this.classes.add(value), remove: value => this.classes.delete(value), contains: value => this.classes.has(value) };
    addEventListener(name, callback, options) {
        const listeners = this.handlers.get(name) ?? [];
        listeners.push({ callback, options });
        this.handlers.set(name, listeners);
    }
    emit(name, event) {
        for (const listener of this.handlers.get(name) ?? [])
            if (!listener.options?.signal?.aborted) listener.callback(event);
    }
    setPointerCapture(id) { this.captured.add(id); }
    hasPointerCapture(id) { return this.captured.has(id); }
    releasePointerCapture(id) { this.captured.delete(id); }
}

function setup() {
    const root = new Element(), viewport = new Element(), canvas = new Element(), image = new Element(), draft = new Element();
    viewport.clientWidth = 1200;
    viewport.clientHeight = 800;
    viewport.scrollLeft = viewport.scrollTop = 0;
    image.naturalWidth = 4800;
    image.naturalHeight = 5159;
    image.complete = true;
    canvas.querySelector = selector => selector === 'img' ? image : draft;
    Object.defineProperty(canvas, 'clientWidth', { get: () => parseFloat(canvas.style.width) || 0 });
    Object.defineProperty(canvas, 'clientHeight', { get: () => parseFloat(canvas.style.height) || 0 });
    canvas.getBoundingClientRect = () => ({ left: 50, top: 60, width: canvas.clientWidth, height: canvas.clientHeight });
    globalThis.ResizeObserver = class { observe() {} disconnect() {} };
    globalThis.document = new Element();
    document.fullscreenElement = null;
    const calls = [];
    map.initialize(root, viewport, canvas, { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } });
    const event = (x, y, extra = {}) => ({ clientX: x, clientY: y, button: 0, pointerId: 1, detail: 1,
        prevented: false, stopped: false, preventDefault() { this.prevented = true; }, stopImmediatePropagation() { this.stopped = true; },
        target: { closest: () => null }, ...extra });
    return { root, viewport, canvas, draft, calls, event };
}

test('zoomed map pans on both axes and suppresses the drag click on a region', () => {
    const { root, viewport, calls, event } = setup();
    map.zoom(root, 2);
    viewport.scrollLeft = 100;
    viewport.scrollTop = 200;
    viewport.emit('pointerdown', event(600, 400, { target: { closest: () => ({ tagName: 'BUTTON' }) } }));
    viewport.emit('pointermove', event(530, 340));
    assert.equal(viewport.scrollLeft, 170);
    assert.equal(viewport.scrollTop, 260);
    assert.ok(viewport.hasPointerCapture(1));
    assert.ok(viewport.classList.contains('is-panning'));
    viewport.emit('pointerup', event(530, 340));
    assert.equal(viewport.hasPointerCapture(1), false);
    assert.equal(viewport.classList.contains('is-panning'), false);
    const click = event(530, 340);
    viewport.emit('click', click);
    assert.ok(click.prevented && click.stopped);
    assert.equal(calls.some(call => call[0] === 'RectangleDrawn'), false);
    const nextClick = event(530, 340);
    viewport.emit('click', nextClick);
    assert.equal(nextClick.stopped, false);
    map.dispose(root);
});

test('short region clicks remain selectable and an open inventory panel does not prevent panning', () => {
    const { root, viewport, event } = setup();
    map.zoom(root, 2);
    map.setState(root, false, true);
    viewport.emit('pointerdown', event(600, 400));
    viewport.emit('pointermove', event(597, 400));
    viewport.emit('pointerup', event(597, 400));
    const click = event(597, 400);
    viewport.emit('click', click);
    assert.equal(click.prevented, false);
    viewport.scrollTop = 100;
    viewport.emit('pointerdown', event(600, 400));
    viewport.emit('pointermove', event(600, 350));
    assert.equal(viewport.scrollTop, 150);
    viewport.emit('pointercancel', event(600, 350));
    assert.equal(viewport.classList.contains('is-panning'), false);
    assert.equal(viewport.hasPointerCapture(1), false);
    map.dispose(root);
});

test('fit mode and non-primary buttons do not start a pan; disposal releases capture and handlers', () => {
    const { root, viewport, event } = setup();
    viewport.emit('pointerdown', event(600, 400));
    viewport.emit('pointermove', event(500, 300));
    assert.equal(viewport.scrollLeft, 0);
    map.zoom(root, 2);
    const zoomedLeft = viewport.scrollLeft;
    viewport.emit('pointerdown', event(600, 400, { button: 2 }));
    viewport.emit('pointermove', event(500, 300));
    assert.equal(viewport.scrollLeft, zoomedLeft);
    viewport.emit('pointerdown', event(600, 400));
    viewport.emit('pointermove', event(500, 300));
    assert.ok(viewport.hasPointerCapture(1));
    map.dispose(root);
    assert.equal(viewport.hasPointerCapture(1), false);
    assert.equal(viewport.classList.contains('is-panning'), false);
    const left = viewport.scrollLeft;
    viewport.emit('pointermove', event(400, 200));
    assert.equal(viewport.scrollLeft, left);
});

test('editing still draws a bounded rectangle without panning', () => {
    const { root, viewport, canvas, draft, calls, event } = setup();
    map.zoom(root, 2);
    map.setState(root, true, false);
    const start = event(50 + canvas.clientWidth * .8, 60 + canvas.clientHeight * .8);
    const end = event(50 + canvas.clientWidth * .2, 60 + canvas.clientHeight * .3);
    canvas.emit('pointerdown', start);
    viewport.emit('pointerdown', start);
    canvas.emit('pointermove', end);
    viewport.emit('pointermove', end);
    canvas.emit('pointerup', end);
    viewport.emit('pointerup', end);
    assert.deepEqual(calls.find(call => call[0] === 'RectangleDrawn'), ['RectangleDrawn', 20, 30, 60, 50]);
    assert.equal(draft.hidden, true);
    assert.equal(viewport.hasPointerCapture(1), false);
    map.dispose(root);
});
