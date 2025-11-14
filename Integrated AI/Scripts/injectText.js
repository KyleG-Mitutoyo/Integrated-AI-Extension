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

function injectTextIntoElement(selector, textToInject) {
    try {
        const elem = document.querySelector(selector);
        if (!elem) {
            return `FAILURE: Input field not found for selector: ${selector}`;
        }

        const style = window.getComputedStyle(elem);
        if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0' || elem.disabled) {
            return `FAILURE: Input field (selector: ${selector}) is not visible or is disabled.`;
        }

        elem.focus();

        const commands = JSON.parse(textToInject);

        // Check the element type and use the appropriate injection method.
        if (elem.tagName.toLowerCase() === 'textarea' || elem.tagName.toLowerCase() === 'input') {
            // METHOD 1: For simple <textarea> and <input> elements (like AI Studio).
            // Reconstruct the plain text with newlines from the command payload.
            let plainText = '';
            commands.forEach(command => {
                if (command.type === 'text') {
                    plainText += command.content;
                } else if (command.type === 'break') {
                    plainText += '\n';
                }
            });

            // Set the cursor to the end before inserting.
            elem.selectionStart = elem.selectionEnd = elem.value.length;

            const currentValue = elem.value || '';
            const separator = currentValue ? '\n' : '';
            const newValue = currentValue + separator + plainText;

            // Use the native value setter for framework compatibility (React, Angular, etc.).
            const prototype = Object.getPrototypeOf(elem);
            const nativeInputValueSetter = Object.getOwnPropertyDescriptor(prototype, 'value').set;
            nativeInputValueSetter.call(elem, newValue);

            elem.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
            return `SUCCESS: Text appended to ${selector} using native value setter method`;

        } else if (elem.isContentEditable) {
            // METHOD 2: For complex contenteditable <div> elements (like ChatGPT, Claude).
            // Move the cursor to the end to ensure we always append.
            const sel = window.getSelection();
            if (sel.rangeCount > 0) {
                const range = document.createRange();
                range.selectNodeContents(elem);
                range.collapse(false); // false collapses to the end
                sel.removeAllRanges();
                sel.addRange(range);
            }

            const hasContent = elem.innerHTML && elem.innerHTML.length > 0 && elem.innerHTML !== '<br>';
            if (hasContent) {
                document.execCommand('insertParagraph', false, null);
            }

            commands.forEach(command => {
                if (command.type === 'text') {
                    document.execCommand('insertText', false, command.content);
                } else if (command.type === 'break') {
                    document.execCommand('insertParagraph', false, null);
                }
            });

            elem.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
            return `SUCCESS: Text appended to ${selector} using execCommand method`;
        }

        return `FAILURE: Element (selector: ${selector}) is neither a text input nor contenteditable.`;
    } catch (e) {
        return `FAILURE: JavaScript error: ${e.message} (Selector: ${selector})`;
    }
}