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
    await check(`document.querySelector('.notes-content strong')?.textContent === 'Research setup' && document.querySelector('.notes-content a')?.getAttribute('href') === 'https://example.com/'`, 'Project notes render Markdown and links without terminating the Blazor circuit.');
    await check(`document.querySelector('[data-field-name="Suggested"] input')?.value === 'initial default'`, 'Unconfigured fields default to a single-line input.');
    await check(`document.querySelector('[data-field-name="Multiline"] textarea')?.value === 'first' + String.fromCharCode(10) + 'second'`, 'Configured multiline fields render a textarea.');
    await check(`!document.querySelector('.field-presentation-picker') && !document.querySelector('.field-mode')`, 'Presentation comes from the field without manual display settings.');
    await check(`!!document.querySelector('[data-field-name="LocalPassword"] input[type=password]') && !!document.querySelector('[data-field-name="Allowed"] select')`, 'Passwords and selection-only fields retain their editors.');
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 4`, 'Configured list parsing preserves empty entries.');
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[1].value === ' beta'`, 'List parsing preserves whitespace.');
    await check(`document.querySelectorAll('[data-field-name="LineFeedList"] .field-list-value').length === 3`, 'List mode uses LineFeed without an explicit separator and normalizes CRLF.');
    await check(`document.querySelectorAll('[data-field-name="TabList"] .field-list-value').length === 2`, 'Escaped Tab separates list entries.');
    await check(`document.querySelector('[data-field-name="ListFixture"] [data-paste-target]')?.disabled === false`, 'Input editors have completed their interactive initialization.');
    await evaluate(`(() => { const input = document.querySelector('[data-field-name="ListFixture"] .field-list-value');
        input.value = 'red;green'; input.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await check(`(() => { const rows = document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value');
        return rows.length === 4 && rows[0].value === 'red' && rows[1].value === 'green' && rows[2].value === '' && rows[3].value === ''; })()`, 'Separated input overwrites following rows and preserves unreached entries.');
    await evaluate(`(() => { const input = document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[3];
        input.value = 'third;fourth'; input.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 5`, 'Separated input appends only missing rows.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] .field-list-add').click()`);
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 6`, 'Add creates an editable list entry.');
    await evaluate(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-row button')[1].click()`);
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 5`, 'Remove deletes the selected entry.');
    await screenshot('input-list-desktop.png');
    await evaluate(`localStorage.setItem('flowblox:input-editor:v1:2c312829-4c61-423c-aa6b-ef85f20e3f1c:ListFixture', JSON.stringify({mode:1,separator:2}))`);
    await send('Page.reload');
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 4`, 'Legacy browser preferences cannot override the configured field mode or separator.');
    await check(`document.querySelector('[data-field-name="Suggested"] input')?.value === 'initial default'`, 'Reload restores input defaults, not cached input values.');
    await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
    await screenshot('input-list-mobile.png');
    await check(`document.documentElement.scrollWidth <= window.innerWidth + 1`, 'The editors fit a narrow mobile viewport.');

    await send('Page.navigate', { url: base + projects[1] });
    await check(`document.querySelector('[data-field-name="ListFixture"] [data-paste-target]')?.disabled === false`, 'The second project is interactive.');
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 1`, 'Another project uses its own configured comma separator.');
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
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] [data-output-project]').length === 2 && !document.querySelector('[data-field-name="ListFixture"] [data-output-project="Unused output fixture"]')`, 'Projects without an existing output directory are excluded.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-output-project]').click()`);
    await check(`!!document.querySelector('[data-field-name="ListFixture"] [data-output-root]')`, 'Selecting an output project opens the directory choices.');
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] [data-output-subdirectory]').length === 20 && !!document.querySelector('[data-field-name="ListFixture"] [data-insert-panel="outputs"] small.muted')`, 'Directory choices stop at 20 with a non-selectable overflow hint.');
    const output = await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        const input = field.querySelector('textarea'); input.setSelectionRange(0, input.value.length);
        const button = field.querySelector('[data-insert-panel="outputs"] [data-field-target]');
        const value = button.dataset.fieldReference; button.click(); return value; })()`);
    await check(`document.querySelector('[data-field-name="ListFixture"] textarea').value === ${JSON.stringify(output)}`, 'A project output directory is inserted into the selected input.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-picker] > summary').click(); document.querySelector('[data-field-name="ListFixture"] [data-insert-view="outputs"]').click()`);
    await check(`!!document.querySelector('[data-field-name="ListFixture"] [data-output-project]')`, 'Reopening the output choices returns to project selection.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-output-project]').click()`);
    await check(`!!document.querySelector('[data-field-name="ListFixture"] [data-output-subdirectory="audio"]')`, 'Artifact subdirectories are available.');
    const nestedOutput = await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        const input = field.querySelector('textarea'); input.setSelectionRange(0, input.value.length);
        const button = field.querySelector('[data-output-subdirectory="audio"]');
        const value = button.dataset.fieldReference; button.click(); return value; })()`);
    await check(`document.querySelector('[data-field-name="ListFixture"] textarea').value === ${JSON.stringify(nestedOutput)} && ${JSON.stringify(nestedOutput)}.startsWith('$Options::Paths.OutputDir')`, 'A nested output directory is inserted while preserving field syntax.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-picker] > summary').click(); document.querySelector('[data-field-name="ListFixture"] [data-insert-view="outputs"]').click()`);
    await check(`!!document.querySelector('[data-field-name="ListFixture"] [data-output-project="Second input fixture"]')`, 'An existing output without subdirectories remains available.');
    await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        const input = field.querySelector('textarea'); input.focus(); input.setSelectionRange(0, input.value.length);
        field.querySelector('[data-output-project="Second input fixture"]').click(); })()`);
    await check(`document.querySelector('[data-field-name="ListFixture"] textarea').value.replaceAll(String.fromCharCode(92), '/') === '$Options::Paths.OutputDir/Second_input_fixture' && !document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]').open && !document.querySelector('[data-field-name="ListFixture"] [data-output-root]')`, 'An output without subdirectories inserts its field reference directly without another selection.');
    await evaluate(`Object.defineProperty(navigator, 'clipboard', { value: { readText: async () => 'clipboard text' } }); true`);
    await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        field.querySelector('[data-insert-picker] > summary').click(); field.querySelector('[data-paste-target]').click(); })()`);
    await check(`document.querySelector('[data-field-name="ListFixture"] textarea').value === 'clipboard text' && !document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]').open`, 'Clipboard insertion uses the shared menu and closes on success.');
    await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        const input = field.querySelector('.field-list-value'); input.value = 'first,second'; input.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value').length === 2`, 'The insertion target can be an individual list entry.');
    await evaluate(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[1].focus()`);
    await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        field.querySelector('[data-insert-picker] > summary').click(); field.querySelector('[data-paste-target]').click(); })()`);
    await check(`document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[0].value === 'first' && document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[1].value === 'clipboard text'`, 'Insert edits the last focused list entry without changing the others.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-picker] > summary').click()`);
    await check(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        const dot = field.querySelector('.field-value-status').getBoundingClientRect();
        const title = field.querySelector('.field-title-text').getBoundingClientRect();
        const menu = field.querySelector('.field-insert-options').getBoundingClientRect();
        return Math.abs(dot.y + dot.height / 2 - title.y - title.height / 2) < 3 && menu.x >= 0 && menu.right <= innerWidth; })()`, 'The dot stays aligned with its title and Insert fits the mobile viewport.');
    await check(`(() => {
        return Array.from(document.querySelectorAll('.inputs .field-heading')).every(heading => {
            const bounds = heading.getBoundingClientRect();
            const dot = heading.querySelector('.field-value-status').getBoundingClientRect();
            const icon = heading.querySelector('.field-value-status .mdi-icon').getBoundingClientRect();
            const title = heading.querySelector('.field-title-text').getBoundingClientRect();
            return Math.abs(dot.right - bounds.right) < 1 && dot.width <= 10.1 && icon.width <= 10.1
                && dot.left > title.right;
        });
    })()`, 'All status dots, including empty fields, align at the right edge of their field headings.');
    await screenshot('input-insert-menu-mobile.png');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-picker] > summary').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))`);
    await check(`!document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]').open`, 'Escape closes Insert.');
    await evaluate(`document.querySelector('[data-field-name="ListFixture"] [data-insert-picker] > summary').click(); document.querySelector('h1').click()`);
    await check(`!document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]').open`, 'Clicking outside closes Insert.');
    await evaluate(`navigator.clipboard.readText = async () => { throw new Error('denied'); }; true`);
    await evaluate(`(() => { const field = document.querySelector('[data-field-name="ListFixture"]');
        field.querySelector('[data-insert-picker] > summary').click(); field.querySelector('[data-paste-target]').click(); })()`);
    await check(`document.querySelector('[data-field-name="ListFixture"] [data-paste-target]').disabled && !document.querySelector('[data-paste-error]') && document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[1].value === 'clipboard text'`, 'Denied clipboard access disables Paste without a hint or input changes.');
    await evaluate(`navigator.permissions.query = async () => ({ state: 'granted' }); navigator.clipboard.readText = async () => ''; true`);
    await evaluate(`refreshPasteButton(document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]'))`);
    await check(`document.querySelector('[data-field-name="ListFixture"] [data-paste-target]').disabled && document.querySelectorAll('[data-field-name="ListFixture"] .field-list-value')[1].value === 'clipboard text'`, 'An empty clipboard disables Paste and preserves existing input.');
    await evaluate(`navigator.clipboard.readText = async () => 'new clipboard text'; refreshPasteButton(document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]'))`);
    await check(`!document.querySelector('[data-field-name="ListFixture"] [data-paste-target]').disabled`, 'Paste becomes available again after copying text.');
    await evaluate(`navigator.permissions.query = async () => ({ state: 'denied' }); refreshPasteButton(document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]'))`);
    await check(`document.querySelector('[data-field-name="ListFixture"] [data-paste-target]').disabled`, 'A denied clipboard permission disables Paste before it is clicked.');
    await evaluate(`navigator.permissions.query = async () => ({ state: 'prompt' }); navigator.clipboard.readText = async () => { throw new Error('Opening Insert must not read without permission'); }; refreshPasteButton(document.querySelector('[data-field-name="ListFixture"] [data-insert-picker]'))`);
    await check(`!document.querySelector('[data-field-name="ListFixture"] [data-paste-target]').disabled`, 'Paste remains available for the explicit user gesture when permission is not yet granted.');
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
