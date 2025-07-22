// Integrated AI
// Copyright (C) 2025 Kyle Grubbs

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any other later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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