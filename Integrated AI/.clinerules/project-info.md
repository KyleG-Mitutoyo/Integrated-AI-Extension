# Project information

I'm making an extension for visual studio 2022 that will allow opening a web window with AI chat on it. It has the ability to move text from the visual studio active document to the AI chat prompt text area and moving text from the AI chat's response to the active document. The "VS to AI" button has 3 modes: moving selected text, the whole file contents, or a function. The "AI to VS" button gets selected text or text copied from an AI chat artifact copy button and moves it to a diff view for comparison with the current document. There is logic to make "auto diff" work by analyzing the AI code for a function, file, or snippet. There is also a backup and restore functionality. The full solution files are backed up when a diff is accepted.

# Framework

This project uses .Net Framework 4.7.2

# Requirements

If possible, I'd like to avoid threading complexity, such as async, await, and joinabletaskfactory.