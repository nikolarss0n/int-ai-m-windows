# Interview Master for Windows: migration plan

Source: the macOS app in `int-ai-m` at commit `7d64c16`. Goal: the same features on Windows 10 2004 (build 19041) and later, in this repo.

## What is being ported

The macOS app is 31,384 lines of Swift in the files `build.sh` compiles, plus about 6,700 lines of tests.

| Layer | Lines | What it holds | Port effort |
|---|---|---|---|
| Domain | 6,175 | Question detection rules, coding-step parsing, practice logic, settings, templates, copy | Translate line for line |
| Application | 3,635 | `VoiceInterviewProcessor`, audio coordinator, export, memory, session analysis | Translate line for line |
| Infrastructure | 3,536 | Groq, Anthropic, xAI, OpenAI and Jev clients; stores; audio and screen capture | Clients and stores translate; 5 Apple-only files are rewritten for Windows |
| Presentation | 16,388 | AppKit window, tabs, code panel, practice, settings, tray menu, hotkeys | Rewritten in Avalonia, screen by screen |
| `interview_master.swift` | 1,650 | App delegate: state and wiring | Rewritten as the composition root |

Twelve tracked Swift files are not in `build.sh` (for example `AnthropicApiClient.swift`, `KeywordMatcher.swift`, `CodingSession.swift`). They are not part of the shipped app and are not ported. The Silero VAD model is also not in the build.

## Stack

- C# 12 on .NET 8, Avalonia 12.2.0 for the UI. Both were checked on the development Mac: the solution builds, and Avalonia renders a window to a PNG without a display.
- The UI is built in code, as the AppKit original is. This also sidesteps Avalonia 12.2's XAML name generator, which needs a newer compiler than SDK 8.0.403 ships.
- Windows-only work goes through Win32 and COM calls.

.NET 8 left support in November 2026. `Directory.Build.props` holds the target framework in one place; move it to `net10.0` once that SDK is installed.

## Solution layout

| Project | Target | Holds |
|---|---|---|
| `src/InterviewMaster.Core` | `net8.0` | Domain, Application, and the platform-neutral Infrastructure (HTTP clients, file stores). Folders mirror the Swift repo so each file can be compared with its source. |
| `src/InterviewMaster.UI` | `net8.0` | Presentation in Avalonia: design tokens, views, controllers, and in `Shell/` the main window, dialogs, tray menu and `AppHost`, which puts the app together. No Windows calls: the operating system comes in through `IPlatformServices`. |
| `src/InterviewMaster.Windows` | `net8.0-windows` | Audio capture, the keyboard hook, capture exclusion, click-through, screenshots. |
| `src/InterviewMaster.App` | `net8.0-windows` | Entry point: one instance, log files, and `WindowsPlatform`, which hands the Windows services to `AppHost`. |
| `tools/InterviewMaster.UiDriver` | `net8.0` | Drives the real UI without a display and writes PNG and text snapshots, like `Scripts/focus-ui-qa` in the macOS repo. Three modes: the interview window, `practice`, and `app` (the whole app on stand-ins for the operating system). Runs on macOS. |
| `tests/InterviewMaster.Core.Tests` | `net8.0` | The nine Swift test files, as xUnit tests. Runs on macOS. |

Dependencies point inward: Core references nothing; UI and Windows reference Core; App references all three. Core reaches the platform only through interfaces it defines (audio source, screen capture, key store, settings store, clock).

## macOS to Windows mapping

| macOS | Windows |
|---|---|
| `window.sharingType = .none` (hidden from screen share) | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` |
| `ignoresMouseEvents` (ghost mode) | `WS_EX_TRANSPARENT` and `WS_EX_LAYERED` window styles |
| `window.level = .floating` | Topmost window |
| `NSVisualEffectView` glass | A dark see-through layer in place of the blur, on the same background-transparency slider |
| Carbon `RegisterEventHotKey` and the event tap | One low-level keyboard hook that keeps the app's shortcuts and passes every other key on |
| ScreenCaptureKit system audio, 48 kHz mono, own audio excluded | WASAPI process loopback that excludes this process tree |
| `AVAudioEngine` microphone | WASAPI capture |
| ScreenCaptureKit screenshots | GDI copy of the primary display |
| `~/.interview-master-keys` | `%USERPROFILE%\.interview-master-keys`, same `KEY=value` format |
| `UserDefaults` | `%APPDATA%\InterviewMaster\settings.json` |
| `~/Documents/InterviewMaster/{sessions,practice}` | `Documents\InterviewMaster\{sessions,practice}`, same file formats |
| PDFKit (CV text) | Not yet: a CV can be Markdown or text. A PDF reader needs a new dependency (open decision 1) |
| Status-bar item "IM" and its menu | Tray icon with the same items, then the commands macOS keeps in its menu bar |
| SF Pro, SF Mono, SF Symbols | Segoe UI Variable, Cascadia Mono, icons drawn in code |
| `NSAlert`, `NSSavePanel` | The app's own dark dialog and the system file picker |
| Console output and `interview_debug.log` in the working folder | `%LOCALAPPDATA%\InterviewMaster\logs\app.log` and `interview_debug.log` |

## Keyboard shortcuts

⌘ becomes Ctrl, ⌥ becomes Alt, ⇧ stays Shift.

| Action | macOS | Windows |
|---|---|---|
| Show or hide the window (global) | ⌘B | Ctrl+B |
| Capture screenshot (global) | ⌘S | Ctrl+S |
| Analyze screenshots (global) | ⌘↩ | Ctrl+Enter |
| Ghost mode (global) | ⌘I | Ctrl+I |
| Context, Timeline, code panel, Practice | ⌘1 to ⌘4 | Ctrl+1 to Ctrl+4 |
| Find in notes | ⌘F | Ctrl+F |
| Clear screenshots | ⌘G | Ctrl+G |
| Settings | ⌘, | Ctrl+, |
| Mute interview audio (hold, or tap to latch) | ⌘⇧M | Ctrl+Shift+M |
| Step through code | ⌘⌥← → | Ctrl+Alt+← → |
| Move question selection | ⌘⌥↑ ↓ | Ctrl+Alt+↑ ↓ |
| Answer over code, copy code | ⌘⌥A, ⌘⌥C | Ctrl+Alt+A, Ctrl+Alt+C |
| Move the window (global) | ⌘⌃ arrows | Ctrl+Alt+Shift+arrows |
| Scroll and move the floating code (global) | ⌘J, ⌘K, ⌘ arrows | Ctrl+J, Ctrl+K, Ctrl+arrows |
| Hide the floating code | ⌘\ | Ctrl+\ |
| New note, export notes, templates, hide window, quit | ⌘N, ⌘E, ⌘T, ⌘M, ⌘Q | Ctrl+N, Ctrl+E, Ctrl+T, Ctrl+M, Ctrl+Q |

Three things to know. Ctrl+Win+arrows is taken by Windows for switching desktops, so moving the window uses Ctrl+Alt+Shift. The keyboard hook keeps the app's shortcuts from other apps exactly as the macOS event tap does: while the app runs, Ctrl+S, Ctrl+B, Ctrl+I and Ctrl+Enter never reach other apps, and Ctrl+G and Ctrl+1 to Ctrl+4 do not while the window is up; whether to keep these chords is an open decision listed at the end. And the last row of the table only works while the main window has the keyboard, which it has on Practice: Windows has no menu bar that stays live when an app is merely in front. The tray menu holds the same commands.

## Phases

Each phase ends with something that can be checked on the Mac. All ten are done there (2026-11-26). What is left is the first run on a Windows PC: [WINDOWS_CHECKLIST.md](WINDOWS_CHECKLIST.md).

1. **Scaffold.** Solution, projects, build settings. Check: `dotnet build` passes.
2. **Domain.** Every Domain file in `build.sh`, with `test_processor`, `test_practice_bank`, `test_speech_turn` and the token-independent parts of `test_design_tokens`. Check: the ported assertions pass.
3. **Application.** `VoiceInterviewProcessor`, `AudioCaptureCoordinator`, the three use cases, with `test_capture_processor`, `test_audio_source`, `test_jev`. The Swift `test_processor` keeps copies of processor functions; the C# tests call the real ones. Check: tests pass with faked transport and clock.
4. **Infrastructure, neutral part.** Streaming clients for Groq, Anthropic, xAI, OpenAI and Jev; memory, practice, CV and key-file stores; debug logger. Check: request bodies and stream parsing match recorded fixtures.
5. **Design tokens and the interview window.** `DesignTokens`, answer card, question list, command bar, header capsule, notices, skeletons. Check: UiDriver snapshots at 1040x680, 760x500, 1600x1000 and 760x900 against the macOS driver's snapshots.
6. **Code panel.** Typed steps, versions per request, follow-up chips, floating fallback window.
7. **Other tabs.** Context (notes editor, markdown, search, formatting toolbar), Timeline, Practice (four card decks, role banks, spoken practice), template selector.
8. **Settings, tray, dialogs.** Settings window, tray menu with the model submenu and transparency slider, shortcuts sheet, permissions check.
9. **Windows services.** Audio capture, hotkeys, capture exclusion, ghost mode, screenshots. Check on the Mac: compiles. These cannot run here.
10. **Packaging.** `scripts/publish.sh` and `scripts/publish.ps1` write `dist/InterviewMaster-win-x64`: one self-contained `InterviewMaster.exe` (about 95 MB), the `Resources` folder and the key-file template. The `.ico` is drawn from the same code as the mark in the window (`UiDriver icon`).

## How it is verified

Everything below runs on the Mac.

- **Against the macOS sources.** `tools/parity/run.sh` compiles the real Swift code, feeds it and the C# port the same inputs, and diffs the outputs. Last run (2026-11-26, after the app was wired up): practice 25,940 lines, API clients 4,048 lines, voice processor 414,786 lines, no differences. See `tools/parity/README.md`.
- **Tests.** 185 xUnit tests (`dotnet test`), including the 31 processor scenarios of `test_capture_processor`, the capture-coordinator lifecycle cases of `test_audio_source`, and the practice recorder, run against faked transport, clocks and audio.
- **The interview window.** `UiDriver <out>` replays the 73 states of the macOS driver; `tools/parity/ui/compare_states.py` compares words and layout with the macOS dump. Last run: 59 of 59 comparable states match.
- **Practice.** `UiDriver practice <out>` replays the macOS practice reference and adds 13 states of its own. On the 36 states that do not depend on a shuffle, every word matches, and frames match within 2 points on 32. The other four differ by one chip that wraps differently in another font, and by chip order where macOS orders by a hash that changes per launch.
- **The app as a whole.** `UiDriver app <out>` builds the app the way the Windows entry point does (`AppHost`, the real shell, dialogs and windows) on stand-ins for audio, the keyboard hook and the native window calls, and checks 73 things: what the window is told at launch, tabs and search, Practice taking and giving back the keyboard, dialogs swallowing shortcuts, closing and quitting with an unfinished session, click-through, moving, every tray command, the floating code's keys, and running with no keyboard hook.
- **Packaging.** `scripts/publish.sh` produces a 64-bit Windows GUI executable. It has not been started.

Not verifiable on the Mac: anything in `InterviewMaster.Windows` and `InterviewMaster.App`, and how Windows itself treats the window. Those compile but have never run. [WINDOWS_CHECKLIST.md](WINDOWS_CHECKLIST.md) lists what to try on a PC, in order.

## Known differences from macOS

The window and the desktop:

- The desktop behind the window is not blurred. A dark layer of the same strength stands in for the blur, so the slider still runs from nearly solid to text only.
- Every window of the app is kept out of screen capture: Settings, dialogs and the transparency panel too. macOS hides only the main window and the floating panels.
- "On all Spaces" has no supported equivalent; Windows has no public API to pin a window to every virtual desktop.
- The tray menu has the status menu's items and, under them, the commands macOS keeps in its menu bar (New Note, Export Notes, Interview Templates, Hide Floating Solution, About, Help, Privacy Policy). A left click on the tray icon shows or hides the window.
- The background-transparency slider opens in a small panel from the tray menu; a Windows tray menu cannot hold a slider.
- Dialogs are the app's own, with the default button first in the row, as Windows has it.
- Ctrl+\ closes the floating code from any app. On macOS ⌘\ works only while the app is in front.
- Ctrl+arrows in the window move it, except while the caret is in a text field, where those keys move by a word.
- After leaving Practice the window may keep the keyboard until another app is clicked once.
- Only one copy of the app runs; a second start exits at once.
- There is no permissions panel. Windows asks for nothing to capture system audio, the screen or the keyboard; a blocked microphone is reported in a dialog that opens the Windows privacy page.
- Screenshots are of the primary display.
- A CV can be Markdown or plain text, not PDF (open decision 1).
- App Store, notarization and DMG material is not ported.

Looks:

- Fonts are the Windows system ones and icons are drawn in code, so text metrics differ slightly from the macOS screenshots.
- Shortcut names in on-screen hints use the Windows chords (for example "Ctrl+S" where macOS shows "⌘S").
- Practice: tick boxes and the date stepper are drawn by the app, and the date is in the system's short format; dropdowns have one chevron; option text in the choice card has a little more padding; the history sheet is opaque; a long response wraps inside its column where macOS clips it; disabled buttons dim more; topic chips outside the interview order appear in the role's own order.
- Settings: the fields start after the longest caption, which is a few points further right than on macOS in a wide font.

Behaviour:

- A spoken practice answer is recorded as a 16 kHz mono WAV, not AAC. Groq accepts both.
- "Topics discussed" in the manual-answer prompt lists topics in the order they came up. macOS lists them in an order that changes on every launch.
- A practice scoring reply with `}` before `{` gives no score. macOS crashes on it.
- A turn cancelled while it is being transcribed shows the same status text as macOS, including the Swift error wording, so saved sessions read the same.

Not ported, because the macOS app does not use it:

- `OpenAIClient.swift` and `KeychainApiKeyStore.swift` are compiled into the macOS app but never called. Keys come from the key file and environment variables, as on macOS.
- The practice burst strip, progress chart, help card and learn option buttons, which no macOS screen shows.
- Auto-hide on screen sharing, which is off on macOS and has no control that turns it on.

## Open decisions

1. Reading a PDF CV needs a PDF text library (PdfPig is the usual choice, MIT licence). The repo rule is no new production dependency without a yes.
2. Keep Ctrl+S, Ctrl+B, Ctrl+I and Ctrl+Enter as global shortcuts, or move them to chords other apps do not use.
3. Whether to add CI on a Windows runner, which needs a GitHub remote. It would build and run the tests on Windows; the checklist would still need a person.
