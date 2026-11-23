window.FlowBloxFieldEditors = (() => {
    const editors = new Map();
    const prefix = 'flowblox:input-editor:v1:';
    const key = (project, field) => prefix + project + ':' + encodeURIComponent(field);
    return {
        attach(id, receiver) { editors.set(id, receiver); },
        detach(id) { editors.delete(id); },
        load(project, field) {
            try { return JSON.parse(localStorage.getItem(key(project, field))); }
            catch { return null; }
        },
        save(project, field, preferences) {
            try { localStorage.setItem(key(project, field), JSON.stringify(preferences)); }
            catch { /* The editor remains usable when browser storage is unavailable. */ }
        },
        setValue(input, value) {
            const receiver = editors.get(input.closest('.field-editor')?.id);
            if (!receiver) return false;
            receiver.invokeMethodAsync('ReceiveTextAsync', input.id, value).catch(() => {});
            return true;
        },
        focus(id) {
            const input = document.getElementById(id);
            input?.focus();
        }
    };
})();

// A native single-line input strips pasted line breaks before Blazor sees oninput.
// Forward the original text so Automatic mode can switch to a textarea without data loss.
document.addEventListener('paste', event => {
    const input = event.target;
    if (!input.matches?.('input[data-auto-multiline]') || input.disabled || input.readOnly) return;
    const text = event.clipboardData?.getData('text/plain');
    if (!text || !/[\r\n]/.test(text)) return;
    const value = input.value.slice(0, input.selectionStart) + text + input.value.slice(input.selectionEnd);
    if (window.FlowBloxFieldEditors.setValue(input, value)) event.preventDefault();
});

function positionInsertMenu(picker) {
    picker.classList.remove('insert-above');
    const options = picker.querySelector('.field-insert-options');
    const bounds = picker.getBoundingClientRect();
    if (window.innerHeight - bounds.bottom < options.offsetHeight + 12 && bounds.top > window.innerHeight - bounds.bottom) {
        picker.classList.add('insert-above');
    }
}

document.addEventListener('toggle', event => {
    const picker = event.target;
    if (!picker.matches?.('[data-insert-picker]') || !picker.open) return;
    for (const panel of picker.querySelectorAll('[data-insert-panel]')) {
        panel.hidden = panel.dataset.insertPanel !== 'main';
    }
    positionInsertMenu(picker);
}, true);

document.addEventListener('click', event => {
    const summary = event.target.closest('[data-insert-picker] > summary');
    if (summary?.getAttribute('aria-disabled') === 'true') {
        event.preventDefault();
        return;
    }
    const button = event.target.closest('[data-insert-view]');
    if (button && !button.disabled) {
        const picker = button.closest('[data-insert-picker]');
        for (const panel of picker.querySelectorAll('[data-insert-panel]')) {
            panel.hidden = panel.dataset.insertPanel !== button.dataset.insertView;
        }
        positionInsertMenu(picker);
        picker.querySelector('[data-insert-panel]:not([hidden]) button')?.focus();
    }
    for (const picker of document.querySelectorAll('[data-insert-picker][open]')) {
        if (!picker.contains(event.target)) picker.open = false;
    }
});

document.addEventListener('keydown', event => {
    const picker = event.target.closest('[data-insert-picker]');
    if (event.key !== 'Escape' || !picker?.open) return;
    picker.open = false;
    picker.querySelector('summary').focus();
    event.preventDefault();
    event.stopPropagation();
});
