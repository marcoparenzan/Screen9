// Drives a headless Edge/Chrome over the DevTools protocol, to check the browser side of the site (WebAssembly pages) without a human:
// evaluate JavaScript, type keys, wait, take screenshots. Needs Node 22+ (global WebSocket). Used for the measurements in docs/web/WASM.md.
//
//   1. Start the site (see docs/web/WASM.md: a published AOT build for speed) and Edge with a remote debugging port:
//        msedge --headless=new --disable-gpu --user-data-dir=%TEMP%\edge-profile --remote-debugging-port=9333 --window-size=1100,900 http://localhost:5199/rally-x
//   2. node ops/browser-drive.mjs <part of the page URL> <command>...
//
// Commands (run in order):
//   click                 focus the first <canvas> (so keys go to the program)
//   keys:<text>           type the characters (a carriage return is Enter, ESC is Escape, a space is Space)
//   wait:<ms>             pause
//   shot:<file.png>       screenshot of the page
//   anything else         a JavaScript expression, evaluated in the page; its value is printed
//
// Example: count the frames a page draws in a second (patch putImageData first, then read the counter):
//   node ops/browser-drive.mjs plasma-demo "window.__n=0; const o=CanvasRenderingContext2D.prototype.putImageData; CanvasRenderingContext2D.prototype.putImageData=function(...a){window.__n++; return o.apply(this,a)}; 1" "wait:1000" "window.__n"
import fs from 'node:fs';
const [, , urlPart, ...cmds] = process.argv;
const list = await (await fetch('http://localhost:9333/json')).json();
const page = list.find(t => t.type === 'page' && t.url.includes(urlPart));
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise(r => ws.onopen = r);
let id = 0; const pending = new Map();
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const sleep = ms => new Promise(r => setTimeout(r, ms));
for (const c of cmds) {
  if (c.startsWith('keys:')) {
    for (const ch of c.slice(5)) {
      const named = { '\r': ['Enter', 'Enter', 13], ' ': [' ', 'Space', 32], '\x1b': ['Escape', 'Escape', 27] }[ch];
      const key = named?.[0] ?? ch, code = named?.[1] ?? (/[a-z]/i.test(ch) ? 'Key' + ch.toUpperCase() : /\d/.test(ch) ? 'Digit' + ch : ch), vk = named?.[2] ?? ch.toUpperCase().charCodeAt(0);
      await send('Input.dispatchKeyEvent', { type: 'keyDown', key, code, windowsVirtualKeyCode: vk, text: named ? (ch === '\r' ? '\r' : ch === ' ' ? ' ' : undefined) : ch });
      await send('Input.dispatchKeyEvent', { type: 'keyUp', key, code, windowsVirtualKeyCode: vk });
      await sleep(60);
    }
  } else if (c.startsWith('shot:')) {
    const r = await send('Page.captureScreenshot', { format: 'png' });
    fs.writeFileSync(c.slice(5), Buffer.from(r.result.data, 'base64'));
  } else if (c.startsWith('wait:')) {
    await sleep(Number(c.slice(5)));
  } else if (c.startsWith('click')) {
    await send('Runtime.evaluate', { expression: "document.querySelector('canvas').focus()" });
  } else {
    const r = await send('Runtime.evaluate', { expression: c, returnByValue: true, awaitPromise: true });
    console.log(JSON.stringify(r.result.result?.value ?? r.result.exceptionDetails ?? r.result));
  }
}
process.exit(0);
