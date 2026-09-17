// Runs without a browser: storage failures must not disable ERP navigation.
const assert = require('node:assert/strict');
const { test } = require('node:test');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');
const source = readFileSync(join(__dirname, '../src/LandErp.Server/wwwroot/js/ui.js'), 'utf8');

function session(storage) {
    const window = {};
    runInNewContext(source, { window, localStorage: storage });
    return window.LandErpUi;
}

function memory(initial = null) {
    const values = new Map(initial === null ? [] : [['landerp.ui.shell.v1', initial]]);
    return {
        getItem: key => values.get(key) ?? null,
        setItem: (key, value) => values.set(key, value),
        values
    };
}

function result(api) {
    return JSON.parse(JSON.stringify(api.loadShellPreferences()));
}

test('First visit uses expanded navigation and comfortable density', () => {
    assert.deepEqual(result(session(memory())), { compact: false, sidebarCollapsed: false });
});

test('Navigation and density persist independently across sessions', () => {
    const storage = memory();
    for (const sidebarCollapsed of [false, true]) {
        for (const compact of [false, true]) {
            session(storage).saveShellPreferences({ compact, sidebarCollapsed });
            assert.deepEqual(result(session(storage)), { compact, sidebarCollapsed });
        }
    }
});

test('Invalid or obsolete stored values fall back safely', () => {
    for (const value of ['{broken', 'null', '[]', '7', '{"compact":"true","sidebarCollapsed":1}']) {
        assert.deepEqual(result(session(memory(value))), { compact: false, sidebarCollapsed: false });
    }
});

test('Only presentation booleans are persisted', () => {
    const storage = memory();
    session(storage).saveShellPreferences({ compact: true, sidebarCollapsed: false, unrelated: 'discard' });
    assert.deepEqual(JSON.parse(storage.getItem('landerp.ui.shell.v1')), { compact: true, sidebarCollapsed: false });
    assert.equal(storage.values.size, 1);
});

test('Blocked storage and quota errors do not prevent navigation', () => {
    const api = session({
        getItem() { throw new Error('Storage blocked'); },
        setItem() { throw new Error('Quota exceeded'); }
    });
    assert.deepEqual(result(api), { compact: false, sidebarCollapsed: false });
    assert.doesNotThrow(() => api.saveShellPreferences({ compact: true, sidebarCollapsed: true }));
});
