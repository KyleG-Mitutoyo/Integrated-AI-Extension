(function() {
    try {
        const elem = document.querySelector(selector);
        if (!elem) {
            return `FAILURE: Input field not found for selector: ${selector}`;
        }

        const style = window.getComputedStyle(elem);
        if (style.display == 'none' || style.visibility == 'hidden' || style.opacity == '0' || elem.disabled) {
            return `FAILURE: Input field (selector: ${selector}) is not visible or is disabled.`;
        }

        if (isChatGpt) {
            elem.style.setProperty('white-space', 'pre-wrap', 'important');
            elem.style.setProperty('line-height', '1.4', 'important');
            elem.style.setProperty('margin', '0', 'important');
            elem.style.setProperty('padding', '0', 'important');
        }

        elem.focus();

        if (elem.tagName.toLowerCase() == 'textarea' || elem.tagName.toLowerCase() == 'input') {
            const currentValue = elem.value || '';
            elem.value = currentValue + (currentValue ? '\n' : '') + textToInject;
            elem.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
            return `SUCCESS: Text appended to ${selector} (value set)`;
        } else if (elem.isContentEditable) {
            const currentContent = elem.innerHTML;
            elem.innerHTML = currentContent + '' + textToInject;
            const range = document.createRange();
            const sel = window.getSelection();
            range.selectNodeContents(elem);
            range.collapse(false);
            sel.removeAllRanges();
            sel.addRange(range);
            elem.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
            return `SUCCESS: Text appended to ${selector} (contenteditable)`;
        }
        return `FAILURE: Element (selector: ${selector}) is neither a text input nor contenteditable.`;
    } catch (e) {
        return `FAILURE: JavaScript error: ` + e.message + ` (Selector: ${selector})`;
    }
})();
