'use strict';

// Offline regression tests for the actual WPF-embedded JS bridge.
// No MAX URL, live session, operator response, real bot page or network is used.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');

const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'src/MaxDesktop.App/MiniAppWindow.cs'), 'utf8');
const match = source.match(/const string script = """([\s\S]*?)""";/);
assert.ok(match, 'Embedded JS bridge must be extracted from production source');
const origin = 'https://unit.example';
const script = match[1].replace('SIGNED_ORIGIN', JSON.stringify(origin));

function start({ pageOrigin = origin, isFrame = false } = {}) {
    const messages = [];
    const page = { chrome: { webview: { postMessage: m => messages.push(JSON.parse(JSON.stringify(m))) } } };
    page.top = isFrame ? {} : page;
    vm.runInNewContext(script, { window: page, location: { origin: pageOrigin } }, { timeout: 1000 });
    return { page, messages };
}

test('top-level ready notification is self-reported, not evidence MAX called our bridge', () => {
    const { page, messages } = start();
    assert.equal(typeof page.WebViewHandler.postEvent, 'function');
    assert.deepEqual(messages, [{ kind: 'miniapp-bridge-ready' }]);
    assert.match(source, /Наш сценарий интерфейса внедрён/);
    assert.doesNotMatch(source, /Интерфейс MAX Bridge активен/);
});

test('trusted same-origin iframe gets a bridge and an explicit frame marker', () => {
    const { page, messages } = start({ isFrame: true });
    assert.equal(typeof page.WebViewHandler.postEvent, 'function');
    assert.equal(typeof page.PrivateWebViewHandler.postEvent, 'function');
    assert.deepEqual(messages, [{ kind: 'miniapp-frame-ready' }]);
});

test('cross-origin iframe never receives a privileged bridge', () => {
    const { page, messages } = start({ isFrame: true, pageOrigin: 'https://foreign.example' });
    assert.equal(page.WebViewHandler, undefined);
    assert.equal(page.PrivateWebViewHandler, undefined);
    assert.deepEqual(messages, []);
});

test('top-level document redirected away from signed origin also receives no bridge', () => {
    const { page, messages } = start({ pageOrigin: 'https://redirect.example' });
    assert.equal(page.WebViewHandler, undefined);
    assert.deepEqual(messages, []);
});

test('only precisely named event with JSON requestId reaches the host, frame and top', () => {
    for (const isFrame of [true, false]) {
        const { page, messages } = start({ isFrame });
        const markerCount = messages.length;
        page.WebViewHandler.postEvent('WebAppReady', '{}');
        page.PrivateWebViewHandler.postEvent('WebAppVerifyMobileId', 'not-json');
        page.PrivateWebViewHandler.postEvent('WebAppVerifyMobileId', JSON.stringify({ url: 'https://unit.example' }));
        page.PrivateWebViewHandler.postEvent('WebAppVerifyMobileId', '{"requestId":"a","noise":"'.concat('x'.repeat(20000), '"}'));
        assert.equal(messages.length, markerCount);
        page.PrivateWebViewHandler.postEvent('WebAppVerifyMobileId',
            JSON.stringify({ requestId: 'synthetic-request-1', url: 'https://unit.example' }));
        assert.deepEqual(messages.at(-1), {
            kind: 'mobile-id-no-cellular', requestId: 'synthetic-request-1'
        });
    }
});

test('frame-originated requests are handled by frame WebMessageReceived and answered in the same frame', () => {
    assert.match(source, /CoreWebView2\.FrameCreated/);
    assert.match(source, /frameArgs\.Frame\.WebMessageReceived \+=/);
    assert.match(source, /HandleMobileIdBridgeMessageAsync\(frameMessage, signedOrigin, frameArgs\.Frame\)/);
    assert.match(source, /sourceFrame is null[\s\S]+ExecuteScriptAsync\(js\)[\s\S]+sourceFrame\.ExecuteScriptAsync\(js\)/);
    assert.match(source, /GetLeftPart\(UriPartial\.Authority\)\.Equals\(signedOrigin/);
    assert.match(source, /Созданных вложенных кадров/);
    assert.doesNotMatch(source, /WebResourceResponseReceived/);
    assert.doesNotMatch(source, /CallDevToolsProtocolMethodAsync/);
});
