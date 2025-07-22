# Integrated AI Extension for Visual Studio 2022

[![Marketplace](https://img.shields.io/visual-studio-marketplace/v/YourPublisher.AI-Code-Companion?style=for-the-badge&label=VS%20Marketplace&color=5C2D91)](https://marketplace.visualstudio.com/items?itemName=YourPublisher.AI-Code-Companion)
[![Version](https://img.shields.io/visual-studio-marketplace/i/YourPublisher.AI-Code-Companion?style=for-the-badge&label=Installs)](https://marketplace.visualstudio.com/items?itemName=YourPublisher.AI-Code-Companion)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](https://www.gnu.org/licenses/gpl-3.0)

Seamlessly bridge your code editor with web AI chats, **no API keys needed!** Refactor, debug, and generate code with a fluid, integrated workflow right inside Visual Studio. This extension is meant for those who want full control of the coding process, rather than vibe coding with agentic code editors, and don't want to mess with API keys, token costs, and limits. You can use this extension with your favorite AI chat, for free or with a subscription that you already have, along with all the features of the chat site such as projects and memory. With Integrated AI, you don't have to give up control and your creative vision to vibe coding, and you also don't have to deal with the old way of copying and pasting code blocks.

The included AI chat sites are Google AI Studio, Grok, Claude, and ChatGPT.

---

### ❌ The Old Workflow

Tired of the endless cycle of `Ctrl+C`, `Alt+Tab`, `Ctrl+V`? Manually copying code between your editor and a browser tab is slow, error-prone, and breaks your focus. You get:

- ❌ Disjointed context switching that kills productivity.
- ❌ Manually finding and replacing code blocks from the AI's response.
- ❌ No easy way to compare the AI's suggestion with your original code.
- ❌ Forgetting to back up your work before making significant AI-suggested changes.

### ✅ With Integrated AI

Integrated AI brings the power of web AI chats directly into your IDE, creating a frictionless development experience.

- ✅ **Stay in the Zone:** A dedicated, dockable web window with multiple AI chat sites to choose from.
- ✅ **One-Click Context:** Send selected code, the current function, or the entire file to the AI in a single click with built-in context.
- ✅ **Intelligent Code Merging:** Review AI suggestions in a familiar diff view before applying them.
- ✅ **Automatic Safety Net:** Your solution is automatically backed up when you accept changes, so you can experiment with confidence.

![Demonstration of the core workflow](https://via.placeholder.com/800x450.gif?text=Feature+GIF+placeholder)
*(More demo GIFs below!)*

## 🚀 Core Features

- **Integrated AI Chat Window**: Open an AI chat web interface in a dockable Visual Studio tool window.
- **🎯 Smart VS-to-AI Transfer**: Use the "VS -> AI" button commands to instantly send code to the prompt text area. Choose from three powerful modes:
  - **Snippet**: Send only the code you've highlighted.
  - **Function**: Choose from the list of functions in the active document.
  - **File**: Send the complete content of the active document.
- **✨ Intelligent AI-to-VS Diffing**: Move code from the AI back to your editor with the "AI -> VS" button commands. You have full control of what gets replaced, with options similar to the VS -> AI commands. The extension can also intelligently analyze the AI's response to find the corresponding function or snippet in your file (turn on Auto Diff and Auto Function Match in the options). It then presents a **rich diff view**, allowing you to review changes line-by-line and accept, decline, or manually choose a different replacement. 
- **🔒 Automatic Backups**: Never lose work. When you accept a diff, the extension automatically creates a zip backup of your entire solution, giving you a restore point. There is also a manual backup button.

## 🛠️ Installation

1.  Open Visual Studio 2022.
2.  Navigate to `Extensions` > `Manage Extensions`.
3.  In the `Browse` tab, search for **"Integrated AI Extension"**.
4.  Click **Download** and follow the instructions to install.
5.  Restart Visual Studio.

## 💡 How to Use: A Quick Start Guide

1.  **Open the Chat Window**: Go to `Tools` > `Open AI Chat Window` > to open the chat tool window which can be docked or moved. Select your desired chat site from the dropdown (default is Google AI Studio).
2.  **Chat with the AI**: Your code will appear in the prompt along with your message. Write your request (e.g., "Refactor this to be more efficient" or "Add exception handling").
3.  **Send Code**: Open a code file. Then click one of the **"VS -> AI"** button options. You can send multiple code blocks or even multiple files, since each one has its own context.
4.  **Review the Diff**: Once the AI provides a code block, select it in the chat window (or use the AI's "copy" button) and click an **"AI -> VS"** command. A diff view will appear, showing the proposed changes.
5.  **Accept or Decline**: If you like the changes, click "Accept" in the diff view. A backup of your solution will be created automatically.

---

### 🎬 Feature Deep Dive

<details>
<summary><b>Demo: Smart "VS to AI" Transfer</b></summary>

See how easy it is to send context to the AI. This shows sending a selected snippet, and then an entire function with just a couple clicks into the same prompt.

_![Sending code from VS to the AI Chat](https://via.placeholder.com/800x450.gif?text=VS-to-AI.gif)_

</details>

<details>
<summary><b>Demo: Intelligent "AI to VS" Diffing</b></summary>

Watch how the extension takes a code block from the AI, automatically finds its place in your source file, and presents a clear, actionable diff. No more manual searching!

_![Applying changes from AI to VS via a diff view](https://via.placeholder.com/800x450.gif?text=AI-to-VS-Diff.gif)_

</details>

<details>
<summary><b>Demo: Automatic Backups</b></summary>

This demonstrates the peace of mind you get from automatic backups. After accepting a diff, a backup is created of the previous solution state.

_![Automatic solution backup on diff acceptance](https://via.placeholder.com/800x450.gif?text=Backup-Feature.gif)_

</details>

## ⚙️ Configuration

You can configure the extension's behavior by going to the gear button to the right of the command buttons.

- **Theme**: Set all windows to light or dark mode.
- **Auto Diff Compare**: Toggle auto AI code detection during code replacement and diffing.
- **Auto Function Match**: Toggle auto function matching by name for Function -> VS command.
- **Create Restore on Accept**: Toggle creation of restore point every time a diff is accepted.
- **Show Log Window**: Used for error reporting and debugging.

## 🚨 Troubleshooting

-   **The AI window is blank or shows an incorrect page?**
    -   Try refreshing the window with Ctrl-R (or right-click the window and select "Refresh") or restarting VS.
-   **A chat page logged me out?**
    -   This is an automatic security feature of some chat pages which happens in a normal browser as well, the only fix is to just log back in.
-   **The "Auto Diff" didn't find the right code for replacement?**
    -   This feature is experimental for now. The diff logic works best when the AI provides a complete function, file, or a clearly defined snippet. If the AI's response is heavily modified or partial, the logic may struggle. Try asking the AI to provide the complete function, file, or snippet in its response.
-   **The "Auto Function Match" didn't find the right function for replacement?**
    -   This is experimental like Auto Diff mode and works best with C# and VB functions.

For any other issues, please report them on the GitHub page.

## 🤝 Connect & Contribute

This is a tool for developers, by a developer. Your feedback is invaluable!

-   🐞 **Report a bug** or **request a feature** by opening an [Issue on GitHub](https://github.com/YourUsername/YourRepo/issues).
-   🛠️ **Contribute** to the project via [Pull Requests](https://github.com/YourUsername/YourRepo/pulls).

## 📄 License

This project is licensed under the GNU General Public License v3.0. This means it is free and open source, and any modifications or distributions must also be licensed under the same terms.

See the [LICENSE](LICENSE) file for the full license text.