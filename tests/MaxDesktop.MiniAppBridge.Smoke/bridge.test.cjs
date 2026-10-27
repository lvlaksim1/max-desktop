'use strict';

// Offline regression tests for the bridge embedded in the WPF MiniAppWindow.
// No MAX credentials, URLs, real mini-app content, or network are used.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');

const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'src/MaxDesktop.App/MiniAppWindow.cs'), 'utf8');
const match = source.match(/const string script = """([\s\S]*?)""";/);
assert.ok(match, 'Expected exact embedded JS bridge source');
const script = match[1].replace('SIGNED_ORIGIN', JSON.stringify('https://unit.example'));

function start({ origin = 'https://unit.example', isFrame = false } = {}) {
    const messages = [];
    const page = {
        chrome: { webview: { postMessage: message => messages.push(JSON.parse(JSON.stringify(message))) } }
    };
    page.top = isFrame ? {} : page;
    vm.runInNewContext(script, { window: page, location: { origin } }, { timeout: 1000 });
    return { page, messages };
}

test('local injected bridge reports ready without any call from MAX', () => {
    const { page, messages } = start();
    assert.equal(typeof page.WebViewHandler.postEvent, 'function');
    assert.deepEqual(messages, [{ kind: 'miniapp-bridge-ready' }]);
    assert.match(source, /Наш сценарий интерфейса внедрён/);
    assert.doesNotMatch(source, /Интерфейс MAX Bridge активен/);
});

test('embedded frames do not receive bridge from current top-frame-only injection', () => {
    const { page, messages } = start({ isFrame: true });
    assert.equal(page.WebViewHandler, undefined);
    assert.deepEqual(messages, []);
});

test('a redirected top document with a different origin receives no bridge', () => {
    const { page, messages } = start({ origin: 'https://other.example' });
    assert.equal(page.WebViewHandler, undefined);
    assert.deepEqual(messages, []);
});

test('unrelated events are ignored, Mobile ID request only creates a negative-branch message', () => {
    const { page, messages } = start();
    page.WebViewHandler.postEvent('WebAppReady', '{}');
    page.PrivateWebViewHandler.postEvent('WebAppVerifyMobileId', 'not-json');
    page.PrivateWebViewHandler.postEvent('WebAppVerifyMobileId', JSON.stringify({ url: 'https://unit.example' }));
    assert.equal(messages.length, 1);

    page.PrivateWebViewHandler.postEvent('WebAppVerifyMobileId',
        JSON.stringify({ requestId: 'offline-req-1', url: 'https://unit.example' }));
    assert.deepEqual(messages[1], { kind: 'mobile-id-no-cellular', requestId: 'offline-req-1' });
    // This JS only notifies its Windows host; no operator request is executed here.
    assert.equal(messages.length, 2);
});

test('the WPF window reports only the count of frames, not their contents or URLs', () => {
    assert.match(source, /CoreWebView2\.FrameCreated/);
    assert.match(source, /Созданных вложенных кадров/);
    assert.match(source, /_frameCreatedCount\+\+/);
});
