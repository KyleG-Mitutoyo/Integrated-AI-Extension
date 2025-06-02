(function () {
    // Store original clipboard write method
    const originalWriteText = navigator.clipboard.writeText;

    // Override navigator.clipboard.writeText
    navigator.clipboard.writeText = async function (text) {
        localStorage.setItem('isProgrammaticCopy', 'true');
        try {
            await originalWriteText.call(navigator.clipboard, text);
        } catch (e) {
            console.error('Clipboard write failed:', e);
        }
        setTimeout(() => {
            localStorage.removeItem('isProgrammaticCopy');
        }, 100);
    };

    // Store original document E
    const originalExecCommand = document.execCommand;

    // Override document.execCommand for 'copy'
    document.execCommand = function (command, ui, value) {
        if (command.toLowerCase() === 'copy') {
            localStorage.setItem('isProgrammaticCopy', 'true');
            setTimeout(() => {
                localStorage.removeItem('isProgrammaticCopy');
            }, 100);
        }
        return originalExecCommand.call(document, command, ui, value);
    };
})();