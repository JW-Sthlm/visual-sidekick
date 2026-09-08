# Security Policy

## Supported versions

Security fixes are applied to the latest version on the `main` branch.

## Report a vulnerability

Use GitHub private vulnerability reporting for this repository. Do not open a public issue with exploit details, screenshots containing sensitive data, access tokens, or personal information.

Include:

- A concise description of the issue.
- Reproduction steps using non-sensitive test data.
- The affected Windows and .NET versions.
- The expected impact.

## Security boundaries

Visual Sidekick captures a window chosen by the user and sends selected frames through the GitHub Copilot SDK. It does not provide an authentication store, remote control, scheduled startup, or a general desktop recording service.

The analysis session has no available tools. Runtime permission requests are rejected, remote sessions are disabled, and conversation continuity is kept in process memory.

The application cannot guarantee that every captured frame is free of sensitive content. Users must pause or stop before revealing passwords, authentication codes, private messages, or regulated data.

## Safe development

Run the safety scan before every public contribution:

```powershell
.\scripts\Test-PublicSafety.ps1
```

The scan rejects personal Windows home paths, work email addresses, managed tenant addresses, tenant identifiers, common token formats, tracked compiled files, and unsafe product positioning.
