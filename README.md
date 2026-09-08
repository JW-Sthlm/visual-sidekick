# Visual Sidekick

Visual Sidekick stays alongside one Windows application window selected by the user. It detects meaningful visual changes, waits until the view settles, then helps you discuss presentations, design reviews, workflows, interfaces, and visible errors.

You remain in control throughout the session. A Windows picker opens at the start, a visible companion panel indicates when live mode is active, and controls for pause, resume, and stop remain available.

## What it does

- Captures only the window or region selected by the user.
- Keeps the current and previous meaningful keyframes in memory.
- Sends a frame for analysis after a meaningful change or when the user asks.
- Opens an always-on-top companion showing its status and controls.
- Keeps follow-up questions in the same visual conversation.

Visual Sidekick is made for review and discussion. Graded assessments, hidden observation, sign-in data collection, and workplace tracking are outside its intended use.

## Requirements

- Windows 10 version 2004 or later, or Windows 11.
- .NET 9 SDK.
- GitHub Copilot CLI installed and signed in.
- Access to the GitHub Copilot SDK through the signed-in account.
- A vision-capable model available to that account.

The project currently targets Windows because it uses WinForms and Windows capture APIs. The Copilot SDK starts a local Copilot runtime, and its APIs may change between package versions. Depending on the account and selected model, this may consume paid requests or premium request capacity.

## Install from source

Clone the repository and run:

```powershell
.\install.ps1
```

Restart any existing Copilot CLI session, then open the visible companion:

```text
/sidekick-start
```

For ask-only use inside the CLI:

```text
/sidekick-ask
```

The installer builds the project into `.local\dist`, which Git ignores, and registers the repository as a Copilot CLI plugin. This repository does not store compiled files.

During the build, the Copilot SDK normally downloads its matching local runtime. If the download is blocked, set `COPILOT_CLI_BINARY_PATH` to an existing Copilot CLI executable. The included PowerShell scripts handle this automatically when `copilot` is available on `PATH`.

## Choose a model

Visual Sidekick works with any vision-capable model supported by your Copilot SDK account. You can set a user-level default during installation:

```powershell
.\install.ps1 -Model "your-model-id"
```

Or set the model for the current shell before starting Copilot CLI:

```powershell
$env:VISUAL_SIDEKICK_MODEL = "your-model-id"
```

The `start_live` MCP tool also accepts a `model` argument. An explicit tool argument takes priority. Visual Sidekick then checks `VISUAL_SIDEKICK_MODEL` before using the built-in default.

## Commands

| Command | Purpose |
|---|---|
| `/sidekick-start` | Select a window and open the live companion |
| `/sidekick-ask` | Start ask-only selected-window capture |
| `/sidekick-region` | Limit capture to part of the selected window |
| `/sidekick-status` | Show the selected target and current state |
| `/sidekick-compare` | Compare the previous keyframe with the current view |
| `/sidekick-pause` | Pause new frame capture |
| `/sidekick-resume` | Resume capture |
| `/sidekick-stop` | Stop and clear buffered frames |

## Privacy controls

Visual Sidekick keeps a rolling pair of current and previous keyframes in memory. Live analysis also uses temporary Copilot SDK runtime storage, which Visual Sidekick attempts to remove when the companion closes normally.

Pause capture before opening passwords, personal messages, health information, customer data, or other sensitive content. Protected or minimized windows may not be capturable. See [PRIVACY.md](PRIVACY.md) for the data flow and [SECURITY.md](SECURITY.md) for reporting guidance.

## Build and test

```powershell
.\build.ps1
.\test.ps1
```

The test script runs the public safety scan and unit tests. It also checks source publishing, window-enumeration smoke coverage, the MCP handshake, and tool registration.

## Uninstall

```powershell
.\uninstall.ps1
```

Add `-RemoveBuildOutput` to remove the ignored local publish folder.

## Project boundaries

Visual Sidekick can analyze only what the capture API returns from the selected window or region. Some GPU-rendered, protected, elevated, or minimized windows may produce blank content or fail to capture.

Automatic comments depend on visual-change thresholds. A small but important edit may therefore need a direct question from the user.

If direct window capture fails, Windows screen-area fallback can include another window overlapping the selected bounds. Keep sensitive content away from that area.

This is an early public project. Read the privacy model before using it with confidential material.

## License

[MIT](LICENSE)