// Render timestamps in the phone/browser time zone, including its daylight-saving rules.
function localizeTimes(root) {
    const nodes = root.querySelectorAll ? root.querySelectorAll('time[datetime]') : [];
    for (const node of nodes) {
        if (node.dataset.localized === node.dateTime) continue;
        const value = new Date(node.dateTime);
        if (!Number.isNaN(value.getTime())) {
            node.textContent = new Intl.DateTimeFormat(undefined, {dateStyle: 'short', timeStyle: 'short'}).format(value);
            node.title = value.toLocaleString();
            node.dataset.localized = node.dateTime;
        }
    }
}
localizeTimes(document);
new MutationObserver(() => localizeTimes(document)).observe(document.body, {childList:true, subtree:true, attributes:true, attributeFilter:['datetime']});

window.flowBloxClipboardAvailable = () => window.isSecureContext && !!navigator.clipboard?.readText;
document.addEventListener('click', event => {
    const button = event.target.closest('[data-field-target]');
    if (!button || button.disabled) return;
    const input = document.getElementById(button.dataset.fieldTarget);
    if (!input || input.disabled || input.readOnly) return;
    input.focus();
    input.setRangeText(button.dataset.fieldReference, input.selectionStart, input.selectionEnd, 'end');
    input.dispatchEvent(new Event('input', { bubbles: true }));
    button.closest('details').open = false;
});
// Read immediately in the user gesture, before a server round trip (important on mobile browsers).
document.addEventListener('click', async event => {
    const button = event.target.closest('[data-paste-target]');
    if (!button || button.disabled) return;
    const hint = button.closest('.field-editor').querySelector('[data-paste-error]');
    try {
        const text = await navigator.clipboard.readText();
        const input = document.getElementById(button.dataset.pasteTarget);
        input.value = text;
        input.dispatchEvent(new Event('input', {bubbles:true}));
        hint.hidden = true;
    } catch { hint.hidden = false; }
});
