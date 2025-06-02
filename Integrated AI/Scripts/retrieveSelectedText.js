function retrieveSelectedText() {
    try {
        let selectedText = window.getSelection().toString();
        if (!selectedText) {
            return 'null';
        }
        return selectedText;
    } catch (error) {
        console.error('retrieveSelectedText ERROR: ' + error.message);
        return 'FAILURE: ' + error.message;
    }
}