window.FlowBloxFieldEditors = (() => {
    const editors = new Map();
    const targets = new Map();
    document.addEventListener('focusin', event => {
        const input = event.target;
        const editor = input.closest?.('.field-editor');
        if (editor && input.matches('input, textarea')) targets.set(editor.id, input.id);
    });
    return {
        attach(id, receiver) { editors.set(id, receiver); },
        detach(id) { editors.delete(id); targets.delete(id); },
        target(button, fallback) {
            const editor = button.closest('.field-editor');
            const active = document.activeElement;
            // Resolve in the browser so opening Insert does not depend on a server focus round trip.
            if (editor?.contains(active) && active.matches('input, textarea')) targets.set(editor.id, active.id);
            const input = document.getElementById(targets.get(editor?.id));
            return input && editor.contains(input) ? input : document.getElementById(fallback);
        },
        insertReference(id, fallback, value) {
            const editor = document.getElementById(id);
            if (!editor) return;
            const input = window.FlowBloxFieldEditors.target(editor, fallback);
            if (!input || input.disabled || input.readOnly) return;
            input.focus();
            input.setRangeText(value, input.selectionStart, input.selectionEnd, 'end');
            input.dispatchEvent(new Event('input', { bubbles: true }));
            editor.querySelector('[data-insert-picker]').open = false;
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
