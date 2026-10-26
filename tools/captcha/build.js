const esbuild = require("esbuild");
const fs = require("node:fs");
const path = require("node:path");

async function main() {
  const root = path.resolve(__dirname, "../../src/MaxDesktop.App/Resources");
  const js = path.join(root, "captcha-sdk.js");
  const css = path.join(root, "captcha-sdk.css");
  await esbuild.build({
    entryPoints: [path.join(__dirname, "entry.js")],
    outfile: js,
    bundle: true,
    minify: true,
    platform: "browser",
    target: "chrome110",
    format: "iife",
    legalComments: "linked",
    logLevel: "info",
  });
  if (!fs.existsSync(css)) fs.writeFileSync(css, "/* SDK uses no external stylesheet */\n");
  if (!fs.existsSync(js) || fs.statSync(js).size < 100)
    throw new Error("Captcha SDK JavaScript bundle is missing");
  // Don't print bundle content, which could include provider details.
  console.log("VK Captcha SDK bundled into app resources");
}
main().catch(err => { console.error(err.message); process.exitCode = 1; });
