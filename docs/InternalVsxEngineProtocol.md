# Internal VSX <-> Engine Notifications (JSON Lines)

This document describes internal, implementation-only messages exchanged between the Visual Studio extension (VSX) and the in-proc debug engine.

## Transport

- Framing is JSON Lines (one JSON object per line, `\n` terminated).
- Messages are sent from the engine to VSX via the dedicated in-proc pipe.

## Message envelope

Engine-to-VSX messages follow the same envelope style:

- `type`: `"event" | "request" | "response"`
- `command`: message name

## Line-number conventions

All `line` fields in these internal notifications are 1-based so that VS automation (`EnvDTE.Breakpoint.FileLine`) can be consumed without extra +1/-1 adjustments.

## Commands

### `breakpoint_changed`

Sent by the engine when a breakpoint is enabled/disabled/changed so the VSX side can keep its breakpoint table in sync.

Payload:

- `changeType`: string (e.g. `"removed"`)
- `file`: string (required, absolute path)
- `line`: int (1-based, required)
- `enabled`: bool (optional)
