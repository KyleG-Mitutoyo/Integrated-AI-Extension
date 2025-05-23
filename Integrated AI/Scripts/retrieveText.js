// retrieveText.js
function retrieveTextFromElement(selector) {
    try {
        const elem = document.querySelector(selector);
        if (!elem) {
            return `FAILURE: Input field not found for selector: ${selector}`;
        }

        const style = window.getComputedStyle(elem);
        if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0' || elem.disabled) {
            return `FAILURE: Input field (selector: ${selector}) is not visible or is disabled.`;
        }

        let text;
        if (elem.tagName.toLowerCase() === 'textarea' || elem.tagName.toLowerCase() === 'input') {
            text = elem.value || '';
        } else if (elem.isContentEditable) {
            text = elem.innerText || '';
        } else {
            return `FAILURE: Element (selector: ${selector}) is neither a text input nor contenteditable.`;
        }
        // The C# side will handle unescaping of \n if ExecuteScriptAsync returns a JSON-encoded string.
        return text;
    } catch (e) {
        return `FAILURE: JavaScript error: ${e.message} (Selector: ${selector})`;
    }
}