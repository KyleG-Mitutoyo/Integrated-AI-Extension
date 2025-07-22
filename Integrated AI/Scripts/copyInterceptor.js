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
    console.log(">>> Copy Interceptor Attached (Signal Mode) <<<");

    const sendSignal = () => {
        const signal = "copy_signal";
        console.log("SUCCESS: Sending signal to C#:", signal);
        window.chrome.webview.postMessage(signal);
    };

    // 1. Intercept the modern Clipboard API
    if (navigator.clipboard && navigator.clipboard.writeText) {
        const originalWriteText = navigator.clipboard.writeText;
        navigator.clipboard.writeText = async function (text) {
            try {
                await originalWriteText.call(navigator.clipboard, text);
                sendSignal(); // Send the simple string signal
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
            sendSignal(); // Send the simple string signal
        }
        return result;
    };
})();