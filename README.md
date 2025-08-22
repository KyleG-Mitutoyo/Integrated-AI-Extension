# Integrated AI Extension for Visual Studio 2022

[![Marketplace](https://img.shields.io/visual-studio-marketplace/v/Kyle-Grubbs.integrated-ai?style=flat&label=VS%20Marketplace&color=5C2D91)](https://marketplace.visualstudio.com/items?itemName=Kyle-Grubbs.integrated-ai)
[![Version](https://img.shields.io/visual-studio-marketplace/i/Kyle-Grubbs.integrated-ai?style=flat&label=Installs)](https://marketplace.visualstudio.com/items?itemName=Kyle-Grubbs.integrated-ai)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=flat)](https://www.gnu.org/licenses/gpl-3.0)

Seamlessly bridge your code editor with web AI chats, **no API keys needed!** Refactor, debug, and generate code with a fluid, integrated workflow right inside Visual Studio. This extension is meant for those who want full control of the coding process, rather than vibe coding with agentic code editors, and don't want to mess with API keys, token costs, and limits. 

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

You can use this extension with your favorite AI chat, for free or with a subscription that you already have, along with all the features you're already used to such as projects and memory. With Integrated AI, you don't have to give up control and your creative vision to vibe coding, and you also don't have to deal with the old way of copying and pasting code blocks.

#### 🤖 Included AI chat sites:

- **Google AI Studio**
- **Grok**
- **Claude**
- **ChatGPT**
- **Gemini**
- **Deepseek**

![Demonstration of the core workflow](https://github.com/KyleG-Mitutoyo/Integrated-AI-Extension/blob/main/assets/main%20demo.gif?raw=true)

## 🚀 Core Features

- **Integrated AI Chat Window**: Open an AI chat web interface in a dockable Visual Studio tool window.
- **🎯 Smart VS-to-AI Transfer**: Use the "VS -> AI" button commands to instantly send code to the prompt text area. Choose from three powerful modes:
  - **Snippet**: Send only the code you've highlighted.
  - **Function**: Choose from the list of functions in the active document.
  - **File**: Send the complete content of the active document.
- **⚠️ Error to AI**: From the error list, right click an error or warning and select "Send to AI chat". It will send the error description and the line that has the error.
- **✨ Intelligent AI-to-VS Diffing**: Move code from the AI back to your editor with the "AI -> VS" button commands. You have full control of what gets replaced, with options similar to the VS -> AI commands. The extension can also intelligently analyze the AI's response to find the corresponding function or snippet in your file (turn on Auto Diff and Auto Function Match in the options). It then presents a **rich diff view**, allowing you to review changes line-by-line and accept, decline, or manually choose a different replacement. 
- **🔒 Automatic Backups**: Never lose work. When you accept a diff, the extension automatically creates a zip backup of your entire solution, giving you a restore point. There is also a manual backup button.

## 🛠️ Installation

1.  Open Visual Studio 2022.
2.  Navigate to `Extensions` > `Manage Extensions`.
3.  In the `Browse` tab, search for **"Integrated AI"**.
4.  Click **Install** and follow the instructions.
5.  Restart Visual Studio.

## 💡 How to Use: A Quick Start Guide

1.  **Open the Chat Window**: In the Visual Studio top bar, go to `Tools` > `Open AI Chat Window` to open the chat tool window which can be docked or moved. Select your desired chat site from the dropdown within the chat window (default is Google AI Studio).
2.  **Chat with the AI**: Your code will appear in the prompt along with your message. Write your request (e.g., "Refactor this to be more efficient" or "Add exception handling").
3.  **Send Code**: Open a code file. Then click one of the **"VS -> AI"** button options. You can send multiple code blocks or even multiple files, since each one has its own context.
4.  **Review the Diff**: Once the AI provides a code block, highlight the text in the chat window (or use the AI's "copy" button) and click an **"AI -> VS"** command. A diff view will appear, showing the proposed changes.
5.  **Accept or Decline**: If you like the changes, click "Accept" in the diff view. A backup of your solution will be created automatically.

---

### 🎬 Feature Deep Dive

<details>
<summary><b>"VS to AI" Transfers</b></summary>

Code is sent to the AI chat with the "-> AI" button commands. Code blocks get a context header so the AI knows useful info such as filepath and type. Using "Function -> AI" opens a selection window with the functions in the active document, as seen in the main demo.

Note: The function commands only work with files that are native to Visual Studio, such as C#, VB, C++, and F#. Files such as XAML and Javascript will still work with snippets and full file transfers.

_![VS to AI commands](https://raw.githubusercontent.com/KyleG-Mitutoyo/Integrated-AI-Extension/refs/heads/main/assets/To%20AI%20commands.png)_

_![Snippet in the prompt area](https://raw.githubusercontent.com/KyleG-Mitutoyo/Integrated-AI-Extension/refs/heads/main/assets/snippet.png)_

_![Function in the prompt area](https://raw.githubusercontent.com/KyleG-Mitutoyo/Integrated-AI-Extension/refs/heads/main/assets/function.png)_

You can send errors or warnings from the VS error list by right-clicking on one and selecting "Send to AI chat". It will paste the error description, line number, and contents of the line into the prompt. It will also navigate to that error in your code, even if a file is closed.

_![Send Error to AI](https://raw.githubusercontent.com/KyleG-Mitutoyo/Integrated-AI-Extension/refs/heads/main/assets/error.png)_

_![Error in the prompt area](https://raw.githubusercontent.com/KyleG-Mitutoyo/Integrated-AI-Extension/refs/heads/main/assets/error%20prompt.png)_

</details>

<details>
<summary><b>Intelligent "AI to VS" Diffing</b></summary>

The "-> VS" commands are used to send highlighted or copied code from the AI chat to your editor. First it will check for highlighted text within the chat window, and if nothing is highlighted it will use whatever is in the clipboard. The code is merged into your existing file automatically, showing a diff view before applying changes. If a different code block to replace is needed, you can use the "Replace Different Code" button. There is also a new file option that will create one with the AI code and add it to the project.

If "Auto Diff" in the options is turned on, you don't even need to use button commands! This only woks with the artifact copy buttons. This is turned off by default. It works best with C# and VB code.

For the "Function -> VS" command, auto matching attempts to find the function to replace by name, or adds it below the last existing function as a new function. This can also be toggled in the options.

_![Replace Different Code window](https://raw.githubusercontent.com/KyleG-Mitutoyo/Integrated-AI-Extension/refs/heads/main/assets/choose%20code.png)_

</details>

<details>
<summary><b>Automatic Backups and Restore Window</b></summary>

After accepting a diff, a backup is created of the previous solution state (this can be disabled in the options). The AI code that was used for that diff and the chat page is also saved to allow for easy searching later.

For restores a separate window opens with different options. There is a list of restores showing the AI code used right after that restore point. If you highlight some AI code in the chat window, the restore window will open to that restore point if it exists. You can also use "Go To Chat" to navigate there. Compare will show multiple diff views with each changed file, and you can use that restore or close the diff views from there.

_![Restore Window](https://raw.githubusercontent.com/KyleG-Mitutoyo/Integrated-AI-Extension/refs/heads/main/assets/restore%20window.png)_

_![Restore Window Compare](https://github.com/KyleG-Mitutoyo/Integrated-AI-Extension/blob/main/assets/compare.gif?raw=true)_

</details>

## ⚙️ Configuration

You can configure the extension's behavior by clicking the gear icon at the top right of the chat window.

- **Theme**: Set all windows to light or dark mode.
- **Auto Diff Compare**: Toggle auto AI code detection during code replacement and diffing.
- **Auto Function Match**: Toggle auto function matching by name for Function -> VS command.
- **Create Restore on Accept**: Toggle creation of restore point every time a diff is accepted.
- **Reset URLs**: URLs to chat pages are saved to make switching chats easy. The last-used URL is also saved when closing VS. This button can be used in case a chat page is incorrect.
- **Show Log Window**: Used for error reporting and debugging.

## 🚨 Troubleshooting

-   **The AI window is blank or shows an incorrect page?**
    -   Try refreshing the window with Ctrl-R (or right-click the window and select "Refresh") or choosing a different chat page. You can also use the "Reset URLs" button in the options.
-   **AI chat page logged me out?**
    -   This is an automatic security feature of some chat pages which happens in a normal browser as well, the only fix is to just log back in.
-   **The "Auto Diff" didn't find the right code for replacement?**
    -   This feature is experimental for now. The diff logic works best when the AI provides a complete function, file, or a clearly defined snippet. If the AI's response is heavily modified or partial, the logic may struggle. Try asking the AI to provide the complete function, file, or snippet in its response.
-   **The "Auto Function Match" didn't find the right function for replacement?**
    -   This is experimental like Auto Diff mode and works best with C# and VB functions.

For any other issues, please report them on the GitHub page.

## 🤝 Connect & Contribute

This is a tool for developers, by a developer. Your feedback is invaluable!

-   ⭐ **Review us** on the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=Kyle-Grubbs.integrated-ai&ssr=false#review-details).
-   🐞 **Report a bug** or **request a feature** by opening an [Issue on GitHub](https://github.com/KyleG-Mitutoyo/Integrated-AI-Extension/issues).
-   🛠️ **Contribute** to the project via [Pull Requests](https://github.com/KyleG-Mitutoyo/Integrated-AI-Extension/pulls).

## 📝 Changelog

- **1.1.4** Small fixes: Fix Function to VS issues, fix use restore button, fix indent issues with Snippet to AI
- **1.1.3** Add Deepseek, small fixes for web navigation, compare views, theme switching, restore message box conflict, new file creation, and indent issues
- **1.1.2** Add markdown code block insertion for sites that support it, optimize new file creation, add status messages for longer operations like file comparing
- **1.1.1** Fix error "cancellation token disposed" when clicking `Replace Different Code` button.
- **1.1.0** Add gemini.google.com to the included chat sites, add "Error to AI" command when right-clicking an error or warning in the VS error list, change new file creation to be on background thread, prevent diff view showing when "new file" is cancelled.
- **1.0.2** Fix theme not setting correctly sometimes on the main chat page, fix URL saving, add `Reset URLs` button
- **1.0.1** Fix README links
- **1.0.0** Initial release

## 📄 License

This project is licensed under the GNU General Public License v3.0. This means it is free and open source, and any modifications or distributions must also be licensed under the same terms.

See the [LICENSE](https://raw.githubusercontent.com/KyleG-Mitutoyo/Integrated-AI-Extension/refs/heads/main/LICENSE) file for the full license text.