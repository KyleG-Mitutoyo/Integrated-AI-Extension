function detectCopySource() {
    console.log('detectCopySource: Initializing copy source detection.');

    let lastClickedElement = null;

    // Track the last clicked element
    document.addEventListener('click', (event) => {
        lastClickedElement = event.target;
        console.log('detectCopySource: Click event on element:', lastClickedElement.tagName, lastClickedElement.className, lastClickedElement.getAttribute('aria-label'));
    }, { capture: true, passive: true });

    // Listen for copy events
    document.addEventListener('copy', (event) => {
        try {
            console.log('detectCopySource: Copy event detected.');
            const selectedText = window.getSelection().toString();
            if (!selectedText) {
                console.log('detectCopySource: No text selected, ignoring.');
                return;
            }
            console.log('detectCopySource: Selected text length:', selectedText.length);

            const isButtonCopy = lastClickedElement &&
                (lastClickedElement.tagName.toLowerCase() === 'button' ||
                    lastClickedElement.getAttribute('role') === 'button' ||
                    lastClickedElement.className.toLowerCase().includes('copy') ||
                    lastClickedElement.getAttribute('aria-label')?.toLowerCase().includes('copy'));

            if (isButtonCopy) {
                console.log('detectCopySource: Button copy detected, sending message.');
                window.chrome.webview.postMessage({
                    type: 'copyCode',
                    text: selectedText
                });
            } else {
                console.log('detectCopySource: Selection copy detected, no message sent.');
            }
        } catch (err) {
            console.error('detectCopySource: Error:', err.message);
        }
    }, { capture: true, passive: true });

    return 'Copy source detection initialized';
}