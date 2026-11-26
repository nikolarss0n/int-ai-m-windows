# First run on Windows: what to check

Nothing in this app has run on Windows yet. It was built and checked on a Mac: the logic by tests, every screen by rendering it without a display, the wiring by running the app on stand-ins for the operating system. The parts below are the ones only a Windows PC can prove. Work through them in order; each step says what should happen.

Needs Windows 10 version 2004 (build 19041) or later, 64-bit.

## Before you start

1. Get the app onto the PC. Either copy `dist\InterviewMaster-win-x64` from a machine that ran `scripts/publish.sh`, or on the PC install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and run `scripts\publish.ps1`.
2. Set the API keys as the [README](../README.md#api-keys) describes.
3. Know where the logs are: `%LOCALAPPDATA%\InterviewMaster\logs`. `app.log` has startup problems, refused system calls and crashes. `interview_debug.log` has the interview pipeline, including what was heard, so treat it as private. Both are cleared on each start.

If a step fails, note the step number and send `app.log`. Remove anything you would not share from `interview_debug.log` before sending it.

## 1. Start and the window

| # | Do | Expect |
|---|---|---|
| 1.1 | Start `InterviewMaster.exe`. | A dark window, 760 by 500, near the top left of the screen, above other windows. A quotation-mark icon in the notification area. No taskbar button. |
| 1.2 | Press Alt+Tab. | The app is not in the list. |
| 1.3 | Start `InterviewMaster.exe` a second time. | Nothing new appears: one window, one tray icon. |
| 1.4 | Look at the window over a bright page. | White text on a dark, slightly see-through sheet with round corners; no black or white rectangle around it. |
| 1.5 | Right-click the tray icon. | This menu, in this order: Show Window, Hide Window, Capture Screenshot, Analyze Screenshots, Start / End Interview, Use Microphone, Answer Model, Background Transparency…, Settings..., Keyboard Shortcuts, New Note, Export Notes..., Interview Templates..., Hide Floating Solution, About, Help, Privacy Policy, Quit. |
| 1.6 | Left-click the tray icon twice. | The window hides, then comes back. |
| 1.7 | Tray menu → Background Transparency…, drag the slider. | The window's background changes while you drag; text stays fully visible. At 100% only text is left. |

## 2. Hidden from screen sharing

This is the feature the app exists for. Check it before anything else is trusted.

| # | Do | Expect |
|---|---|---|
| 2.1 | Press Win+Shift+S and select the whole screen. | The app's window is not in the picture. |
| 2.2 | Share the whole screen in Teams, Zoom or Meet with a second device watching. | The viewer does not see the window. |
| 2.3 | Open Settings from the tray menu, then repeat 2.1. | The Settings window is not in the picture either. |
| 2.4 | Tray menu → Keyboard Shortcuts, then repeat 2.1. | The dialog is not in the picture. |

On Windows 10 before version 2004 a window cannot be left out; it shows as a black box and `app.log` says so. If 2.1 shows the window on a newer system, `app.log` has the error number from `SetWindowDisplayAffinity`.

## 3. The window does not take the keyboard

| # | Do | Expect |
|---|---|---|
| 3.1 | Type in Notepad, click a tab button in the app, keep typing. | The letters keep going to Notepad. Notepad's title bar stays active. |
| 3.2 | Drag the window by an empty part of it. | It moves. It may only jump to the new place when you let go: Windows does that to windows that never take the keyboard. Ctrl+Alt+Shift+arrows always works. |
| 3.3 | Drag an edge or a corner. | The window resizes, and not below 760 by 500. |
| 3.4 | Press Ctrl+I, then click where the window is. | The click lands on whatever is behind. A notice in the window says clicks pass through. Ctrl+I again takes clicks back. |
| 3.5 | Click the × in the window. | The window hides. Ctrl+B brings it back. |

## 4. Shortcuts from any app

Do these with Notepad in front.

| # | Do | Expect |
|---|---|---|
| 4.1 | Ctrl+B, twice. | The window hides and shows with a short fade. |
| 4.2 | Ctrl+1, Ctrl+2, Ctrl+4, Ctrl+2. | Context, Timeline, Practice, Timeline. |
| 4.3 | Ctrl+S in Notepad with the app running. | A screenshot is taken by the app; Notepad's Save dialog does not open. |
| 4.4 | Ctrl+C and Ctrl+V in Notepad. | They work as always. |
| 4.5 | Type with AltGr (for example AltGr+E for € on many keyboards). | The character is typed; the app does nothing. |
| 4.6 | Hide the window (Ctrl+B), then press Ctrl+1 in a browser. | The browser switches to its first tab: with the window hidden these keys are left alone. |
| 4.7 | Ctrl+Alt+Shift+arrows. | The window moves a step each press and stops at the edge of the screens. |
| 4.8 | Tray menu → Keyboard Shortcuts while Notepad is in front, then Ctrl+2. | Nothing changes behind the dialog. |

If none of these work, `app.log` says "Shortcuts work only while the window has the keyboard" with the reason the keyboard hook was refused. Some antivirus tools block such hooks.

## 5. Listening and answering

Needs the Groq key and one answer key.

| # | Do | Expect |
|---|---|---|
| 5.1 | Press the green Start. | "Interview started. Listening for questions…" and a timer in the title row. |
| 5.2 | Play a video in which someone asks a technical question. | The level bars move while it plays. Within a few seconds the question appears and an answer streams in. |
| 5.3 | Play the app's own sounds or music from another app at the same time. | Still works. On Windows 10 the app may hear its own sound as well; from Windows 11 it leaves it out. |
| 5.4 | Press the microphone button. | The first time, a note explains what the microphone hears. After "Use Microphone" the source switches and your voice is heard as the interviewer. |
| 5.5 | Hold Ctrl+Shift+M while speaking, then let go. | The capsule shows muted while held and nothing you say is transcribed. A short tap latches the mute until the next tap. |
| 5.6 | Ask for code ("write a function that reverses a string"). | The code panel opens beside the answer and types the code in steps. Ctrl+Alt+← and → step through it; Ctrl+Alt+C copies it. |
| 5.7 | Press End. | "Interview ended", and a notice that the session was saved, with a button that opens the folder in Explorer. The file is in `Documents\InterviewMaster\sessions`. |
| 5.8 | Turn off Settings → Privacy & security → Microphone → "Let desktop apps access your microphone", then choose the microphone in the app. | The app says the microphone is off and offers to open that settings page. |

## 6. Screenshots

| # | Do | Expect |
|---|---|---|
| 6.1 | With a coding problem on screen, press Ctrl+S. | A thumbnail appears in the timeline. The app's own window is not in the screenshot. |
| 6.2 | Hide the window, press Ctrl+S. | A small note with the thumbnail appears at the top right for a moment. |
| 6.3 | Press Ctrl+Enter. | The screenshots are analysed and the solution appears. With the window hidden it appears in a floating panel at the top right. |
| 6.4 | With the floating panel up and another app in front: Ctrl+J, Ctrl+K, Ctrl+arrows, Ctrl+\\. | It scrolls, moves, and closes. The same keys still reach the app in front. |
| 6.5 | Ctrl+G on the Timeline. | The screenshots are cleared. |

Only the primary display is captured.

## 7. Practice

| # | Do | Expect |
|---|---|---|
| 7.1 | Ctrl+4, then click in the answer box and type. | The window takes the keyboard here, and only here. Typing works. |
| 7.2 | Start a run, type an answer, press Ctrl+Enter. | The key ideas are revealed, as the button would do. |
| 7.3 | Press "Answer aloud", speak, press it again. | "Recording answer…", then your words appear in the box. |
| 7.4 | Close the window with an unfinished run. | "Close Interview Master?" with Save & Close, Cancel and Discard. |
| 7.5 | Ctrl+2, then type. | The app is back on the Timeline. The window may keep the keyboard until you click another app once; after that it never takes it again. |
| 7.6 | Tray menu → Quit with an unfinished run. | The same question as 7.4; Cancel keeps the app running. |

## 8. Settings, notes and files

| # | Do | Expect |
|---|---|---|
| 8.1 | Tray menu → Settings..., change the role, Save. | The title row of the main window shows the new role. Captions are not cut off by the fields beside them. |
| 8.2 | Settings → Attach…, pick a `.md` or `.txt` CV. | Its name appears next to CV. PDF is not offered in this build. |
| 8.3 | Tray menu → Interview Templates..., pick one. | Its text fills the Context tab. |
| 8.4 | Tray menu → Export Notes... | A save dialog; the file is written. |
| 8.5 | Export button in the bar after an interview. | A save dialog offering Markdown and JSON. |
| 8.6 | Restart the app. | Role, languages, transparency and notes are as you left them. Keys typed in Settings are gone; keys in the key file are still there. |

Typing into the Context tab is not possible, as on macOS: the window only takes the keyboard on Practice. Notes come from templates and New Note.

## What is most likely to need fixing

These are the places where the code relies on Windows behaviour that could not be tried:

1. **Capture exclusion and the tool-window style on a borderless Avalonia window** (section 2, step 1.2).
2. **The keyboard hook** and its key-repeat detection (section 4, step 5.5).
3. **WASAPI capture**: the process loopback that leaves out the app's own sound, sample-rate conversion, and device changes mid-interview (section 5).
4. **Dragging a window that never takes the keyboard** (step 3.2) and **giving the keyboard back after Practice** (step 7.5).
5. **The screen-reader announcements** for notices: they go through an invisible live region and have not been heard with Narrator.
6. **The see-through background**: a dark layer stands in for the macOS blur. If Windows draws the window opaque, `MainWindow.cs` is where the transparency hint is set.
