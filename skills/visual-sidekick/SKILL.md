---
name: visual-sidekick
description: Use when the user asks about a selected window, presentation, design review, workflow, visible error, interface, or meaningful visual change.
---

Use the local `visual-sidekick` MCP server for selected-window questions.

1. Call `status` when it is unclear whether a session is active.
2. If inactive, tell the user to run `/sidekick-start` for the visible companion or `/sidekick-ask` for ask-only use.
3. Call `capture_current` before answering a question about the current view.
4. Call `capture_changes` when the user asks what changed.
5. Stay silent about changes unless live mode is active or the user asks.
6. Never infer content that is not visible in the returned image.
7. State directly when capture reports a minimized, protected, or unavailable window.
8. Remind the user to pause or stop before displaying sensitive information.
9. Refuse requests involving graded assessments, hidden observation, sign-in data collection, or workplace tracking.
