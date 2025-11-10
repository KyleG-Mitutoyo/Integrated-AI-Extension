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

(function () {
    if (window.copyInterceptorAttached) {
        return;
    }
    window.copyInterceptorAttached = true;
    console.log(">>> Copy Interceptor Attached (Capture and Clear Mode) <<<");

    // This variable will hold the selection captured just before a potential copy action.
    let selectionCapturedOnMouseDown = "";

    // STEP 1: CAPTURE PHASE LISTENER
    // This is the key to winning the race condition. It runs on mousedown,
    // before the website's own click handlers have a chance to clear the selection.
    document.addEventListener('mousedown', () => {
        const selection = window.getSelection().toString();
        // Cache the selection if it exists. If the user clicks on non-text,
        // the selection will be empty, effectively clearing our cache for this action.
        selectionCapturedOnMouseDown = selection;
    }, true); // `true` is critical. It forces this listener to run in the capture phase.


    // STEP 2: The trigger function that uses our captured value.
    const sendSignal = () => {
        const selectionPrefix = "copy_signal_with_selection::";
        const simpleSignal = "copy_signal";

        // Use the value we captured on mousedown, NOT the live selection.
        if (selectionCapturedOnMouseDown && selectionCapturedOnMouseDown.trim().length > 0) {
            const message = selectionPrefix + selectionCapturedOnMouseDown;
            console.log("SUCCESS: Sending selection captured on mousedown to C#.");
            window.chrome.webview.postMessage(message);
        } else {
            console.log("SUCCESS: No selection was captured on mousedown, sending simple signal.");
            window.chrome.webview.postMessage(simpleSignal);
        }
        
        // STEP 3: CLEAR the cache immediately after using it.
        // This prevents a stale value from being used on a future copy
        // (e.g., a keyboard Ctrl+C) where no mousedown occurred.
        selectionCapturedOnMouseDown = "";
    };

    // The hooks for clipboard actions now only serve as triggers for sendSignal.
    
    // 1. Intercept the modern Clipboard API
    if (navigator.clipboard && navigator.clipboard.writeText) {
        const originalWriteText = navigator.clipboard.writeText;
        navigator.clipboard.writeText = async function (text) {
            try {
                await originalWriteText.call(navigator.clipboard, text);
                sendSignal();
            } catch (e) {
                console.error('Clipboard write failed:', e);
            }
        };
    }

    // 2. Intercept the legacy execCommand
    const originalExecCommand = document.execCommand;
    document.execCommand = function (command) {
        const result = originalExecCommand.apply(document, arguments);
        if (command && command.toLowerCase() === 'copy' && result) {
            sendSignal();
        }
        return result;
    };
})();