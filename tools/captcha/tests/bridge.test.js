const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const html = fs.readFileSync(path.resolve(__dirname, "../../../src/MaxDesktop.App/Resources/max-captcha.html"), "utf8");
const script = html.match(/<script>([\s\S]*?)<\/script>/i)?.[1];
assert.ok(script, "page must contain bridge logic");
const sent = [];
const listeners = {};
let captchaHandler;
let renderedOptions;
class FakeCaptchaWidget {
  on(event, callback) {
    assert.equal(event, "success");
    captchaHandler = callback;
  }
  async render(options) { renderedOptions = options; }
}
const context = {
  URL,
  console,
  window: {
    MaxDesktopCaptchaSdk: { CaptchaWidget: FakeCaptchaWidget, CaptchaPublicEvents: { SUCCESS: "success" } },
    chrome: { webview: {
      postMessage: obj => sent.push(obj),
      addEventListener: (name, listener) => { listeners[name] = listener; }
    }}
  }
};
vm.runInNewContext(script, context);
assert.equal(sent[0].type, "ready");
listeners.message({data:{action:"open",url:"https://id.vk.ru/not_robot_captcha?session_token=synthetic"}});
setImmediate(() => {
  assert.equal(renderedOptions.container, "#captcha");
  assert.equal(sent.at(-1).type, "shown");
  captchaHandler({token:"synthetic-human-captcha-token"});
  captchaHandler({token:"synthetic-human-captcha-token"});
  assert.equal(sent.filter(x=>x.type==="solved").length, 1, "must pass the success exactly once");
  assert.equal(sent.at(-1).token, "synthetic-human-captcha-token");
  assert.equal(sent.some(x=>x.type==="open"), false);
  console.log("VK CAPTCHA SDK callback → WPF bridge: PASS");
});
