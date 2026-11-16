const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

test('failed or timed-out Browser Print writes are not resent to a fallback host', async () => {
    for (const response of ['HTTP', 'timeout']) {
        const writes = [];
        const context = { window: {}, location: { protocol: 'http:' }, console: { warn() {} }, fetch: async (url) => {
            if (url.endsWith('/available')) {
                if (url.startsWith('http:')) throw new Error('Unavailable');
                return { ok: true, text: async () => JSON.stringify({ printer: [{ uid: 'selected' }] }) };
            }
            writes.push(url);
            if (response === 'timeout') throw new Error('Timed out after send');
            return { ok: false, status: 500 };
        } };
        const script = fs.readFileSync(path.resolve(__dirname, '../../../06_Web/AMES.Web/wwwroot/js/zebra-print.js'), 'utf8');
        vm.runInNewContext(script, context);
        const result = await context.window.amesZebraWrite('selected', '^XA^PQ1^XZ', true);
        assert.equal(result.ok, false);
        assert.deepEqual(writes, ['https://127.0.0.1:9101/write']);
    }
});
