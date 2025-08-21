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

function injectTextIntoElement(selector, textToInject, isChatGptSite, isClaudeSite, isGeminiSite) {
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

        if (elem.tagName.toLowerCase() === 'textarea' || elem.tagName.toLowerCase() === 'input') {
            // For frameworks like React, directly setting elem.value doesn't trigger change handlers.
            // We need to use the native value setter and then dispatch an input event.
            const currentValue = elem.value || '';
            const newValue = currentValue + (currentValue ? '\n' : '') + textToInject;

            const prototype = Object.getPrototypeOf(elem);
            const nativeInputValueSetter = Object.getOwnPropertyDescriptor(prototype, 'value').set;
            nativeInputValueSetter.call(elem, newValue);

            // Dispatching the event is still crucial for the framework to pick up the change
            elem.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));

            return `SUCCESS: Text appended to ${selector} (native value set)`;
        } 
        else if (elem.isContentEditable) {
            
            if (isClaudeSite || isGeminiSite) {
                // THE DEFINITIVE SOLUTION: Parse a JSON payload of commands from C#.
                // This completely avoids any "magic string" delimiters.
                const commands = JSON.parse(textToInject);

                if (elem.innerHTML.length > 0 && !elem.innerHTML.endsWith('<br>')) {
                    document.execCommand('insertParagraph', false, null);
                }

                commands.forEach(command => {
                    if (command.type === 'text') {
                        // The content is the original line of code, with literal '\n' preserved.
                        document.execCommand('insertText', false, command.content);
                    } else if (command.type === 'break') {
                        // This is a structural line break.
                        document.execCommand('insertParagraph', false, null);
                    }
                });

            } else { // This is your working logic for ChatGPT, etc.
                const currentContent = elem.innerHTML;
                elem.innerHTML = currentContent + textToInject;
            }

            // The cursor logic and event dispatching are still valuable.
            const range = document.createRange();
            const sel = window.getSelection();
            range.selectNodeContents(elem);
            range.collapse(false);
            sel.removeAllRanges();
            sel.addRange(range);

            elem.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
            return `SUCCESS: Text appended to ${selector} via execCommand loop`;
        }
        
        return `FAILURE: Element (selector: ${selector}) is neither a text input nor contenteditable.`;
    } catch (e) {
        return `FAILURE: JavaScript error: ${e.message} (Selector: ${selector})`;
    }
}