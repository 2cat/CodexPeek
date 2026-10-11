# Code signing policy

CodexPeek is preparing an application to the SignPath Foundation free open source code signing program. Approval has not been granted, and current builds are unsigned.

If approved, the intended arrangement is: Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org). The certificate publisher will be SignPath Foundation.

## Responsibilities

- Author and reviewer: [2cat](https://github.com/2cat), repository owner and maintainer.
- Signing approver: [2cat](https://github.com/2cat). Each release requires the maintainer's manual approval.
- Other contributors' changes must be reviewed before inclusion in a release.

The maintainer must enable multi-factor authentication for GitHub and SignPath before signing is enabled. This document does not assert that either account has already enabled it.

## Builds and signing

Release binaries will be compiled from this repository by the [Windows checks workflow](https://github.com/2cat/CodexPeek/actions/workflows/ci.yml). Only the project's own executable may be signed. Node.js and Codex must be installed separately from their official sources.

The executable product name is `CodexPeek`; its product version matches `package.json`. SignPath configuration and credentials will be added after application approval. A local build alone does not establish a verified signing provenance.

## Privacy policy

CodexPeek reads local Codex task titles, public execution events, reply summaries and account usage limits to show them on the user's Windows desktop. Local task data is read through Codex's named pipe and read-only session files/database; CodexPeek does not send task data to the maintainer or SignPath, add telemetry, or create a task history database.

For usage limits, CodexPeek starts the user's installed Codex CLI App Server. That component may contact OpenAI using the user's existing signed-in account. Clicking a task opens its conversation in Codex. OpenAI services are covered by [OpenAI's privacy policy](https://openai.com/policies/privacy-policy/).

Diagnostic output and the optional `--diagnostics` file can contain task content and local paths. Review and redact them before sharing. Preview mode reads a user-supplied local JSON file. The app does not modify Windows security settings.

## Installation and removal

Extract a release into its own folder and run `CodexPeek.exe` with `backend.mjs` and `status.mjs` beside it. Node.js 24+, the npm-installed Codex CLI and the Codex desktop app are prerequisites. The app does not automatically create a startup entry.

To remove CodexPeek, exit it from the tray menu, remove any shortcut you created in `shell:startup`, then delete the extracted app folder. Node.js and Codex are separate installations.
