import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

let element;
let lastScroll;
const container = {
    scrollTop: 200, clientTop: 0, clientHeight: 400, scrollHeight: 1200,
    getBoundingClientRect: () => ({ top: 68 }),
    scrollTo: value => { lastScroll = value; }
};
const context = {
    window: {},
    document: { addEventListener() {}, querySelector: () => element }
};
vm.runInNewContext(readFileSync(new URL('../src/05_Pda/AMES.Pda/wwwroot/js/pda-scan.js', import.meta.url), 'utf8'), context);
for (const [targetTop, expectedTop] of [[568, 550], [-500, 0], [1500, 800]]) {
    element = {
        closest: selector => { assert.equal(selector, '.pda-bd'); return container; },
        getBoundingClientRect: () => ({ top: targetTop, height: 100 }),
        scrollIntoView: () => assert.fail('Must not scroll the document or navigation')
    };
    context.window.pdaScan.scrollTo('.location-panel');
    assert.equal(lastScroll.top, expectedTop);
    assert.equal(lastScroll.behavior, 'smooth');
}
lastScroll = null;
element = null;
context.window.pdaScan.scrollTo('.missing');
element = { closest: () => null };
context.window.pdaScan.scrollTo('.outside-content');
assert.equal(lastScroll, null);
console.log('PASS: scan scrolling stays inside the content area and clamps to its bounds.');
