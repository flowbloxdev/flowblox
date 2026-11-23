// Optional interactive checks for Test-WebServer.ps1. Node 22+ and installed Chrome/Edge; no npm dependencies.
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [base, profile, results] = process.argv.slice(2);
const executable = [process.env.FLOWBLOX_TEST_BROWSER,
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(file => file && existsSync(file));
if (!executable) throw new Error('Install Chrome/Edge or set FLOWBLOX_TEST_BROWSER.');
mkdirSync(profile, { recursive: true });
mkdirSync(results, { recursive: true });
const browser = spawn(executable, ['--headless=new', '--disable-gpu', '--no-first-run', '--disable-background-networking',
    '--remote-debugging-port=0', '--window-size=1280,1000', `--user-data-dir=${profile}`, 'about:blank'],
    { windowsHide: true, stdio: 'ignore' });
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
const checks = [];
let socket;
let send;

async function waitFor(action, message) {
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
        if (await action()) return;
        await pause(100);
    }
    throw new Error(message);
}

async function evaluate(expression) {
    const response = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (response.exceptionDetails) throw new Error(response.exceptionDetails.text + ': ' + response.result?.description);
    return response.result.value;
}

async function check(expression, name) {
    await waitFor(() => evaluate(expression), name);
    checks.push(name);
    console.log('Passed: ' + name);
}

async function select(field, selector, value) {
    await evaluate(`(() => { const e = document.querySelector('[data-field-name="${field}"] ${selector}');
        e.value = ${JSON.stringify(value)}; e.dispatchEvent(new Event('change', { bubbles: true })); })()`);
    await pause(200);
}

async function screenshot(name) {
    const image = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
    writeFileSync(join(results, name), Buffer.from(image.data, 'base64'));
}

try {
    await waitFor(() => existsSync(join(profile, 'DevToolsActivePort')), 'Browser did not expose its debugging port.');
    const port = readFileSync(join(profile, 'DevToolsActivePort'), 'utf8').split('\n')[0].trim();
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    socket = new WebSocket(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve); socket.addEventListener('error', reject); });
    let sequence = 0;
    const pending = new Map();
    socket.addEventListener('message', event => {
        const message = JSON.parse(event.data);
        if (!pending.has(message.id)) return;
        const { resolve, reject, timeout } = pending.get(message.id);
        clearTimeout(timeout);
        pending.delete(message.id);
        message.error ? reject(new Error(message.error.message)) : resolve(message.result);
    });
    send = (method, params = {}) => new Promise((resolve, reject) => {
        const id = ++sequence;
        const timeout = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 20000);
        pending.set(id, { resolve, reject, timeout });
        socket.send(JSON.stringify({ id, method, params }));
    });
    await send('Page.enable');
    await send('Page.navigate', { url: base + '/' });
    await check(`document.querySelectorAll('a[href^="/projects/"]').length >= 2`, 'Synthetic projects are available.');
    const projects = await evaluate(`Array.from(document.querySelectorAll('a[href^="/projects/"]')).map(a => a.getAttribute('href'))`);
    await send('Page.navigate', { url: base + projects[0] });
    await check(`document.querySelector('[data-field-name="Suggested"] input')?.value === 'initial default'`, 'Automatic mode defaults to a single-line input.');
    await check(`document.querySelector('[data-field-name="Multiline"] textarea')?.value === 'first\\nsecond'`, 'Existing line breaks automatically render multiline.');
    await check(`!document.querySelector('[data-field-name="LocalPassword"] .field-mode') && !document.querySelector('[data-field-name="Allowed"] .field-mode')`, 'Passwords and selection-only fields retain their editors.');
    await pause(800);
    await evaluate(`(() => { const input = document.querySelector('[data-field-name="Suggested"] input'); input.select();
        const data = new DataTransfer(); data.setData('text/plain', 'pasted\\nsecond line');
        input.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true })); })()`);
    await check(`document.querySelector('[data-field-name="Suggested"] textarea')?.value === 'pasted\\nsecond line'`, 'Pasting multiline text preserves line breaks and expands Automatic mode.');
    await select('Suggested', '.field-mode', 'SingleLine');
    await check(`!!document.querySelector('[data-field-name="Suggested"] input')`, 'Explicit Single line overrides automatic expansion.');
    await select('Suggested', '.field-mode', 'Multiline');
    await check(`document.querySelector('[data-field-name="Suggested"] textarea')?.value === 'pasted\\nsecond line'`, 'Mode changes preserve the original field value.');

    await select('ListFixture', '.field-mode', 'List');
    await select('ListFixture', '.field-separator', 'Semicolon');
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 4`, 'List parsing preserves empty entries.');
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[1].value === ' beta'`, 'List parsing preserves whitespace.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] .field-list-add').click()`);
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 5`, 'Add creates an editable list entry.');
    await evaluate(`(() => { const rows = document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value');
        const input = rows[rows.length - 1]; input.value = 'gamma'; input.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await pause(200);
    await evaluate(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-row button')[1].click()`);
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 4`, 'Remove deletes the selected entry.');
    await select('ListFixture', '.field-mode', 'Multiline');
    await check(`document.querySelector('[data-field-name="ListFixture"] textarea')?.value === 'alpha;;;gamma'`, 'Edited list entries serialize with the selected separator.');
    await select('ListFixture', '.field-mode', 'List');
    await select('ListFixture', '.field-separator', 'Comma');
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 1`, 'Separator changes reinterpret the value without rewriting it.');
    await evaluate(`(() => { const input = document.querySelector('[data-field-name="ListFixture"] .field-list-value');
        input.value = 'red,green'; input.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 2`, 'Entering a comma-separated value creates separate list entries.');
    await select('ListFixture', '.field-mode', 'Multiline');
    await evaluate(`(() => { const input = document.querySelector('[data-field-name="ListFixture"] textarea');
        input.value = 'alpha;beta;gamma'; input.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await pause(150);
    await select('ListFixture', '.field-mode', 'List');
    await select('ListFixture', '.field-separator', 'Semicolon');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] .field-presentation-picker').open = true`);
    await screenshot('input-list-desktop.png');

    const preferences = await evaluate(`Object.entries(localStorage).filter(([key]) => key.startsWith('flowblox:input-editor:v1:'))`);
    if (!preferences.length || preferences.some(([key, value]) => !key.includes('2c312829-4c61-423c-aa6b-ef85f20e3f1c') || /pasted|gamma|alpha/.test(value))) {
        throw new Error('Preferences must use the internal project GUID and contain no field values.');
    }
    checks.push('Preferences contain only display settings, keyed by the internal project GUID.');
    await send('Page.reload');
    await check(`document.querySelector('[data-field-name="ListFixture"] .field-mode')?.value === 'List'`, 'List mode is restored without running or saving a project.');
    await check(`document.querySelector('[data-field-name="ListFixture"] .field-separator')?.value === 'Semicolon'`, 'The separator is restored independently.');
    await check(`document.querySelector('[data-field-name="Suggested"] textarea')?.value === 'initial default'`, 'Reload restores input defaults, not cached input values.');
    await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
    await screenshot('input-list-mobile.png');
    await check(`document.documentElement.scrollWidth <= window.innerWidth + 1`, 'The editors fit a narrow mobile viewport.');

    await send('Page.navigate', { url: base + projects[1] });
    await check(`!!document.querySelector('[data-field-name="ListFixture"] [data-paste-target]')`, 'The second project is interactive before changing preferences.');
    await check(`document.querySelector('[data-field-name="ListFixture"] .field-mode')?.value === 'Automatic'`, 'Another internal project GUID has independent preferences.');
    await evaluate(`Object.defineProperty(window, 'localStorage', { value: { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } } }); true`);
    await select('ListFixture', '.field-mode', 'Multiline');
    await check(`!!document.querySelector('[data-field-name="ListFixture"] textarea')`, 'Blocked browser storage does not prevent changing the editor.');
    await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        field.scrollIntoView({ block: 'center' }); field.querySelector('[data-insert-picker] > summary').click(); })()`);
    await check(`document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]').open`, 'The shared Insert menu opens next to the input.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-view="fields"]').click()`);
    await check(`!document.querySelector('[data-field-name="ListFixture"] [data-insert-panel="fields"]').hidden`, 'Field selection opens within the same menu.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-panel="fields"] [data-insert-view="main"]').click()`);
    await check(`!document.querySelector('[data-field-name="ListFixture"] [data-insert-panel="main"]').hidden`, 'Back returns to the Insert choices without closing the menu.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-view="fields"]').click()`);
    const reference = await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        const input = field.querySelector('textarea'); input.setSelectionRange(0, input.value.length);
        const button = field.querySelector('[data-insert-panel="fields"] [data-field-target]');
        const value = button.dataset.fieldReference; button.click(); return value; })()`);
    await check(`document.querySelector('[data-field-name="ListFixture"] textarea').value === ${JSON.stringify(reference)} && !document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]').open`, 'Choosing a field replaces the selected text and closes Insert.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-picker] > summary').click()`);
    await check(`!document.querySelector('[data-field-name="ListFixture"] [data-insert-panel="main"]').hidden`, 'Reopening Insert returns to the main choices.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-view="outputs"]').click()`);
    const output = await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        const input = field.querySelector('textarea'); input.setSelectionRange(0, input.value.length);
        const button = field.querySelector('[data-insert-panel="outputs"] [data-field-target]');
        const value = button.dataset.fieldReference; button.click(); return value; })()`);
    await check(`document.querySelector('[data-field-name="ListFixture"] textarea').value === ${JSON.stringify(output)}`, 'A project output directory is inserted into the selected input.');
    await evaluate(`Object.defineProperty(navigator, 'clipboard', { value: { readText: async () => 'clipboard text' } }); true`);
    await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        field.querySelector('[data-insert-picker] > summary').click(); field.querySelector('[data-paste-target]').click(); })()`);
    await check(`document.querySelector('[data-field-name="ListFixture"] textarea').value === 'clipboard text' && !document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]').open`, 'Clipboard insertion uses the shared menu and closes on success.');
    await select('ListFixture', '.field-mode', 'List');
    await select('ListFixture', '.field-separator', 'Comma');
    await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        const input = field.querySelector('.field-list-value'); input.value = 'first,second'; input.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 2`, 'The insertion target can be an individual list entry.');
    await evaluate(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[1].focus()`);
    await pause(200);
    await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        field.querySelector('[data-insert-picker] > summary').click(); field.querySelector('[data-paste-target]').click(); })()`);
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[0].value === 'first' && document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[1].value === 'clipboard text'`, 'Insert edits the last focused list entry without changing the others.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-picker] > summary').click()`);
    await check(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        const dot = field.querySelector('.field-value-status').getBoundingClientRect();
        const title = field.querySelector('.field-title-text').getBoundingClientRect();
        const menu = field.querySelector('.field-insert-options').getBoundingClientRect();
        return Math.abs(dot.y + dot.height / 2 - title.y - title.height / 2) < 3 && menu.x >= 0 && menu.right <= innerWidth; })()`, 'The dot stays aligned with its title and Insert fits the mobile viewport.');
    await screenshot('input-insert-menu-mobile.png');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-picker] > summary').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))`);
    await check(`!document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]').open`, 'Escape closes Insert.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-picker] > summary').click(); document.querySelector('h1').click()`);
    await check(`!document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]').open`, 'Clicking outside closes Insert.');
    await evaluate(`navigator.clipboard.readText = async () => { throw new Error('denied'); }; true`);
    await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        field.querySelector('[data-insert-picker] > summary').click(); field.querySelector('[data-paste-target]').click(); })()`);
    await check(`!document.querySelector('[data-field-name="ListFixture"] [data-paste-error]').hidden && document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[1].value === 'clipboard text'`, 'Denied clipboard access shows the native-paste hint without changing the input.');
    writeFileSync(join(results, 'result.json'), JSON.stringify({ passed: true, checks }, null, 2));
} catch (error) {
    if (send) await screenshot('failure.png').catch(() => {});
    writeFileSync(join(results, 'result.json'), JSON.stringify({ passed: false, checks, error: String(error) }, null, 2));
    throw error;
} finally {
    if (send) await send('Browser.close').catch(() => {});
    socket?.close();
    await Promise.race([new Promise(resolve => browser.once('exit', resolve)), pause(3000)]);
    if (browser.exitCode === null) browser.kill();
}
