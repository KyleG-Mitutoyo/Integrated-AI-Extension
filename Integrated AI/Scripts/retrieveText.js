(function() {
    try {
        const elem = document.querySelector('{escapedSelectorForJs}');
        if (!elem) {
            return `FAILURE: Input field not found for selector: ${escapedSelectorForJs.Replace("`", "\\`")}`;
        }

        const style = window.getComputedStyle(elem);
        if (style.display === 'none' || style.visibility == 'hidden' || style.opacity == '0' || elem.disabled) {
            return `FAILURE: Input field (selector: ${escapedSelectorForJs.Replace("`", "\\`")}) is not visible or is disabled.`;
        }

        let text;
        if (elem.tagName.toLowerCase() == 'textarea' || elem.tagName.toLowerCase() == 'input') {
            text = elem.value || '';
        } else if (elem.isContentEditable) {
            text = elem.innerText || ''; // Use innerText to avoid HTML tags
        } else {
            return `FAILURE: Element (selector: ${escapedSelectorForJs.Replace("`", "\\`")}) is neither a text input nor contenteditable.`;
        }

        // Normalize newlines and remove literal escape sequences
        text = text.replace(/\\\\n/g, '\\n');
        return text;
    } catch (e) {
        return `FAILURE: JavaScript error: ` + e.message + ` (Selector: ${escapedSelectorForJs.Replace("`", "\\`")})`;
    }
})();
