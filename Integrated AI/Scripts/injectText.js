// injectText.js
function injectTextIntoElement(selector, textToInject, isChatGptSite) {
    try {
        const elem = document.querySelector(selector);
        if (!elem) {
            return `FAILURE: Input field not found for selector: ${selector}`;
        }

        const style = window.getComputedStyle(elem);
        if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0' || elem.disabled) {
            return `FAILURE: Input field (selector: ${selector}) is not visible or is disabled.`;
        }

        if (isChatGptSite) {
            // Apply styles specific to ChatGPT-like text areas if needed
            elem.style.setProperty('white-space', 'pre-wrap', 'important');
            elem.style.setProperty('line-height', '1.4', 'important');
            elem.style.setProperty('margin', '0', 'important');
            elem.style.setProperty('padding', '0', 'important');
        }

        elem.focus();

        if (elem.tagName.toLowerCase() === 'textarea' || elem.tagName.toLowerCase() === 'input') {
            const currentValue = elem.value || '';
            // The textToInject comes pre-formatted (plain text or HTML string) from C#
            elem.value = currentValue + (currentValue ? '\n' : '') + textToInject;
            elem.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
            return `SUCCESS: Text appended to ${selector} (value set)`;
        } else if (elem.isContentEditable) {
            const currentContent = elem.innerHTML;
            // The textToInject for contentEditable (especially for ChatGPT) is an HTML string
            elem.innerHTML = currentContent + textToInject;

            // Move cursor to the end
            const range = document.createRange();
            const sel = window.getSelection();
            range.selectNodeContents(elem);
            range.collapse(false); // false to collapse to end, true to collapse to start
            sel.removeAllRanges();
            sel.addRange(range);

            elem.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
            return `SUCCESS: Text appended to ${selector} (contenteditable)`;
        }
        return `FAILURE: Element (selector: ${selector}) is neither a text input nor contenteditable.`;
    } catch (e) {
        return `FAILURE: JavaScript error: ${e.message} (Selector: ${selector})`;
    }
}