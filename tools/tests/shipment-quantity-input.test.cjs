const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

let onInput;
vm.runInNewContext(readFileSync(path.join(__dirname,
    '../../src/06_Web/AMES.Web/wwwroot/js/shipment-quantity-input.js'), 'utf8'), {
    document: {
        addEventListener(name, handler, capture) {
            assert.equal(name, 'input');
            assert.equal(capture, true);
            onInput = handler;
        }
    }
});

for (const [typed, expected] of [['01', '1'], ['0010', '10'], ['000', '0'],
    ['0', '0'], ['', ''], ['10', '10'], ['100', '100'], ['0.5', '0.5']]) {
    test(`${JSON.stringify(typed)} becomes ${JSON.stringify(expected)} before the bound value is read`, () => {
        const target = { value: typed, matches: selector => selector === 'input[data-shipment-qty]' };
        onInput({ target });
        assert.equal(target.value, expected);
    });
}

test('does not change other inputs, disabled fields, or text composition', () => {
    for (const settings of [{ matches: () => false }, { disabled: true }, { readOnly: true }, { composing: true }]) {
        const target = { value: '01', matches: () => true, ...settings };
        onInput({ target, isComposing: settings.composing });
        assert.equal(target.value, '01');
    }
});
