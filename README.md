# Interview Master for Windows

The Windows version of Interview Master, the interview copilot that listens to the interviewer, detects questions, and streams short answer cues. It is a port of the macOS app with the same features, written in C# on .NET 8 with Avalonia.

**Status:** the port is complete in code and has not yet run on Windows. It was built and checked on a Mac: the logic against the macOS sources, every screen by rendering it, and the app's wiring on stand-ins for the operating system. Audio capture, the keyboard hook and hiding from screen sharing can only be proven on a PC. Before relying on it, work through [docs/WINDOWS_CHECKLIST.md](docs/WINDOWS_CHECKLIST.md). The plan, the macOS-to-Windows mapping and the known differences are in [docs/MIGRATION_PLAN.md](docs/MIGRATION_PLAN.md).

## Get it running

Needs Windows 10 version 2004 or later, 64-bit, and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build.

```powershell
scripts\publish.ps1
dist\InterviewMaster-win-x64\InterviewMaster.exe
```

The script writes one self-contained `InterviewMaster.exe` (about 95 MB, no .NET install needed to run it) with its `Resources` folder. Keep the two together. `scripts/publish.sh` does the same from macOS or Linux.

To run from source while developing: `dotnet run --project src/InterviewMaster.App`.

The app has no taskbar button. It lives in the notification area: right-click the quotation-mark icon for the menu, left-click it to show or hide the window. Set your keys first, as below.

## Shortcuts

They work from any app while Interview Master runs.

| Keys | Does |
| --- | --- |
| Ctrl+B | Show or hide the window |
| Ctrl+S | Capture a screenshot |
| Ctrl+Enter | Analyze the screenshots |
| Ctrl+I | Let clicks pass through the window |
| Ctrl+1, Ctrl+2, Ctrl+4 | Context, Timeline, Practice |
| Ctrl+3 | Show or hide the code panel |
| Ctrl+G | Clear the screenshots |
| Ctrl+Shift+M | Mute interview audio: hold, or tap to latch |
| Ctrl+Alt+↑ ↓ | Move between questions |
| Ctrl+Alt+← →, Ctrl+Alt+C, Ctrl+Alt+A | Step through code, copy it, show the answer over it |
| Ctrl+Alt+Shift+arrows | Move the window |

Ctrl+S, Ctrl+B, Ctrl+I and Ctrl+Enter do not reach other apps while Interview Master runs. Quit it from the tray menu to get them back.

## Where it keeps things

| What | Where |
| --- | --- |
| API keys | `%USERPROFILE%\.interview-master-keys` |
| Settings and notes | `%APPDATA%\InterviewMaster\settings.json` |
| Saved interviews, practice history, interview memory | `Documents\InterviewMaster` |
| Logs, cleared on each start | `%LOCALAPPDATA%\InterviewMaster\logs` |

The logs can hold what was said in an interview.

## API keys

The app calls model providers with your own keys. You need a Groq key and one answer-model key.

| Key | Needed | Used for | Where to get it |
| --- | --- | --- | --- |
| `GROQ_API_KEY` | Required | Speech-to-text, the fast answer path, practice mode | <https://console.groq.com/keys> |
| `XAI_API_KEY` | Recommended | Answers, question classification, screenshot analysis (Grok) | <https://console.x.ai> |
| `ANTHROPIC_API_KEY` | Fallback | The same as xAI, used only when `XAI_API_KEY` is not set | <https://platform.claude.com/settings/keys> |
| `OPENAI_API_KEY` | Optional | Answers, only when you pick a GPT model in the model menu | <https://platform.openai.com/api-keys> |
| `TYPESAFE_API_KEY` | Optional | Experimental Jev question classification, off by default | Your Typesafe account, see <https://docs.typesafe.ai/api> |

To get a key, sign in to the provider's console at the link, create an API key, and copy it right away. Most providers show a key only once. Some need billing or credits set up before the key works.

### Set the keys in the key file

Keys live in one text file in your user profile: `%USERPROFILE%\.interview-master-keys`, for example `C:\Users\you\.interview-master-keys`.

1. In PowerShell, from this folder, copy the template and open it:

   ```powershell
   Copy-Item interview-master-keys.example "$env:USERPROFILE\.interview-master-keys"
   notepad "$env:USERPROFILE\.interview-master-keys"
   ```

2. Fill in the values and save:

   ```
   GROQ_API_KEY=your_groq_key
   XAI_API_KEY=your_xai_key
   ```

3. Restart the app. Keys are read when it starts.

Rules for the file:

- One `NAME=value` per line, with no quotes around the value.
- Lines that start with `#` are ignored.
- Keep the name `.interview-master-keys` exactly. If you save from Notepad with **Save As**, choose **All files** so it does not add `.txt`.

### Or use environment variables

A variable with the same name overrides the file. To set one for your Windows account:

```powershell
setx GROQ_API_KEY "your_groq_key"
setx XAI_API_KEY "your_xai_key"
```

`setx` applies to programs started afterwards, so restart the app.

### Keep keys private

- Never commit the filled-in key file or paste its contents into logs, issues, or chat.
- The file sits in your user profile, which other standard Windows accounts on the PC cannot read.
- A key changed in the app's Settings window applies to the running app only. Edit the file to keep it.
- If a key leaks, revoke it in the provider's console and create a new one.

### What each provider receives

- Groq receives interview and practice audio for transcription. For fast answers it also receives the transcript and your CV text if you attached one.
- xAI (or Anthropic, or OpenAI when a GPT model is selected) receives the transcribed conversation, your CV text if you attached one, and screenshots you capture.
- Typesafe receives recent transcript text, not audio, and only when the Jev experiment is on (set `IM_JEV_ENABLED=1`).

## Tests and checks

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). All of these run on Windows, macOS and Linux, with no network, no audio device and no keys:

```powershell
dotnet test tests/InterviewMaster.Core.Tests
dotnet run --project tools/InterviewMaster.UiDriver -- shots/interview
dotnet run --project tools/InterviewMaster.UiDriver -- practice shots/practice
dotnet run --project tools/InterviewMaster.UiDriver -- app shots/app
```

The first runs the unit tests. The other three draw the real screens without a display and write a PNG and a text dump per state: the interview window, the practice tab, and the whole app on stand-ins for the operating system. The `app` run ends with a count of checks passed and failed.

Comparing the port with the macOS app needs a Mac and a checkout of the macOS repo; see [tools/parity/README.md](tools/parity/README.md).
