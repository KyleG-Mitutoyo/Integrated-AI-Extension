(function () {
    if (window.copyInterceptorAttached) {
        return;
    }
    window.copyInterceptorAttached = true;

    // 1. Intercept the modern Clipboard API
    if (navigator.clipboard && navigator.clipboard.writeText) {
        const originalWriteText = navigator.clipboard.writeText;

        navigator.clipboard.writeText = async function (text) {
            try {
                await originalWriteText.call(navigator.clipboard, text);
                // SUCCESS: Send the actual text that was copied.
                window.chrome.webview.postMessage({
                    type: 'programmatic_copy_complete',
                    text: text 
                });
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
            // For execCommand, we get the text from the current selection.
            const selectedText = window.getSelection().toString();
            window.chrome.webview.postMessage({
                type: 'programmatic_copy_complete',
                text: selectedText
            });
        }
        return result;
    };
})();