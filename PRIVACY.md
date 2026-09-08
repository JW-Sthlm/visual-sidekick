# Privacy

Visual Sidekick is designed around explicit selection and visible operation.

## Data flow

1. The user starts a session and selects one visible application window.
2. Capture happens locally on Windows.
3. The service compares reduced visual signatures locally to detect meaningful changes.
4. A selected frame is sent through the GitHub Copilot SDK only after a meaningful change in live mode, or after a direct user question.
5. The companion displays the response and remains visible while live mode is active.

## Local state

The service keeps the current frame and the previous meaningful keyframe in memory. Stop clears the selected window, region, and buffered images. Pause stops new captures without clearing the current session.

The Copilot SDK uses a temporary runtime directory for each live analysis session. Visual Sidekick provides no tools to the model, rejects runtime permission requests, disables remote sessions and session storage, and attempts to remove the temporary directory on normal shutdown.

The preferred capture method reads the selected window directly. When Windows does not return usable content, a screen-area fallback may include another window that overlaps the selected bounds. Move unrelated or sensitive windows away from the selected area.

The MCP start response includes the selected window title and process name in the current Copilot CLI conversation. Visual Sidekick does not send the list of other open window titles through its MCP tools.

## User responsibilities

Select only content you are allowed to share with the configured Copilot service. Pause or stop before showing passwords, authentication prompts, personal messages, regulated data, or confidential customer information.

Do not use Visual Sidekick for graded assessments, hidden observation, collection of sign-in data, or workplace tracking.

## Network processing

Image analysis is performed through the GitHub Copilot SDK and the chosen model. Network handling, retention, billing, and regional processing are governed by the terms and settings of the user's GitHub Copilot account.

## No background startup

The repository does not install a scheduled task, Windows service, startup entry, or background launcher. A session begins only after a user runs a command and chooses a window.
