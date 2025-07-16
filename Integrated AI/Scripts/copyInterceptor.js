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