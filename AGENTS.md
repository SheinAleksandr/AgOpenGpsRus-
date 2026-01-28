# AGENTS.md

## Purpose
Repository-level guidance for Codex/agent behavior in this project.

## Project context
- Solution root: `SourceCode`
- Main app: `SourceCode/GPS`
- Radar implementation: `SourceCode/GPS/Classes/CRadar*.cs` and `SourceCode/GPS/Classes/UsbCanZlg.cs`

## Workflow
- Prefer minimal, targeted changes.
- Avoid modifying generated designer files unless required.
- Use `rg` for searches; read the smallest relevant file(s) first.

## Code style
- Keep edits ASCII unless the file already uses Unicode.
- Maintain existing formatting and naming patterns.
- Add brief comments only if logic is non-obvious.

## Safety
- Do not delete or revert existing changes unless asked.
- Do not run destructive commands unless explicitly requested.
