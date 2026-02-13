# Remote Debugger VSX ⇄ Remote Runtime JSON Protocol

**Version**: 1.0  
**Status**: publish-ready

> HTML version: `docs/RemoteProtocol.html` (generated). Regenerate with:
>
> `powershell -NoProfile -ExecutionPolicy Bypass -File tools/GenerateRemoteProtocolHtml.ps1`

This document specifies the **JSON Lines** protocol between the Visual Studio extension (**VSX**) and the **Remote runtime**.

---

## Change Log

- **v1.0**: Initial release of the Remote Debugger VSX ↔ Remote Runtime JSON Protocol specification.

---

## Contents

- [1. Conformance language](#1-conformance-language)
- [2. Scope](#2-scope)
- [3. Transport](#3-transport)
- [4. Message envelope](#4-message-envelope)
- [5. Error model](#5-error-model)
- [6. Commands](#6-commands)
  - [6.1 Command summary](#61-command-summary)
  - [6.2 Session commands](#62-session-commands)
    - [6.2.1 `start`](#621-start)
    - [6.2.2 `ready`](#622-ready)
    - [6.2.3 `stop`](#623-stop)
  - [6.3 Run-control commands](#63-run-control-commands)
    - [6.3.1 `continue`](#631-continue)
    - [6.3.2 `step`](#632-step)
    - [6.3.3 `pause` (OPTIONAL)](#633-pause-optional)
  - [6.4 Breakpoint management](#64-breakpoint-management)
    - [6.4.1 `set_breakpoint`](#641-set_breakpoint)
    - [6.4.2 `remove_breakpoint`](#642-remove_breakpoint)
  - [6.5 Inspection commands (REQUIRED)](#65-inspection-commands-required)
    - [6.5.1 `get_threads`](#651-get_threads)
    - [6.5.2 `get_stack`](#652-get_stack)
    - [6.5.3 `get_scope`](#653-get_scope)
    - [6.5.4 `get_property`](#654-get_property)
    - [6.5.5 `get_evaluation`](#655-get_evaluation)
    - [6.5.6 `set_variable`](#656-set_variable)
- [7. Events](#7-events)
  - [7.1 Event summary](#71-event-summary)
  - [7.2 `stopped`](#72-stopped)
  - [7.3 `continued`](#73-continued)
  - [7.4 `program_exited`](#74-program_exited)
  - [7.5 `output`](#75-output)
  - [7.6 `thread_started` / `thread_exited`](#76-thread_started--thread_exited)
- [8. Data conventions](#8-data-conventions)
- [9. Ordering, state, and resilience](#9-ordering-state-and-resilience)
- [10. Examples](#10-examples)
- [11. Conformance checklist](#11-conformance-checklist)
  - [11.1 General](#111-general)
  - [11.2 Run-control](#112-run-control)
  - [11.3 Breakpoints](#113-breakpoints)
  - [11.4 Inspection (REQUIRED)](#114-inspection-required)
- [12. Implementation Guidelines](#12-implementation-guidelines)

---

## 1. Conformance language

The key words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are to be interpreted as described in RFC 2119.

---

## 2. Scope

- This document is the **only** externally relevant contract for the Remote runtime implementer.
- The Remote runtime treats the Visual Studio side as a single protocol consumer (VSX).
- Implementation details inside the Visual Studio side (internal components/notifications) are intentionally **out of scope**.

---

## 3. Transport

- Framing is **JSON Lines**: one JSON object per line.
- Encoding MUST be UTF-8.
- Each message MUST be terminated by `\n`.
- Receivers SHOULD tolerate `\r\n`.

Operational recommendations:

- Flush after each line.
- Keep message size reasonable (e.g., < 1MB per line) to avoid UI lag.

---

## 4. Message envelope

Every message MUST be a single JSON object and MUST contain:

- `type`: `"request"` | `"response"` | `"event"`

### 4.1 Requests

Requests MUST contain:

| Field | Type | Description |
|---|---:|---|
| `type` | string | Always `"request"` |
| `command` | string | Command name |
| `seq` | integer | VSX-generated correlation id |

Notes:

- `seq` SHOULD be monotonically increasing for the lifetime of a connection.
- On a new connection (reconnect), `seq` MAY restart (e.g., from `1`).
- Within a single connection, if a remote detects a duplicate `seq` still considered in-flight, it SHOULD respond `success=false` with a descriptive `message`.

### 4.2 Responses

Responses MUST contain (minimum schema):

| Field | Type | Description |
|---|---:|---|
| `type` | string | Always `"response"` |
| `command` | string | Same command name as request |
| `success` | boolean | `true` or `false` |
| `requestSeq` | integer | Echo of request `seq` |
| `message` | string | REQUIRED when `success=false`; OPTIONAL when `success=true` (consumers ignore it on success). |

**Important consumer behavior**:

- If the VSX-side consumer receives `success=false` without a non-empty `message`, it may treat the response as invalid and ignore it.
-- When `success=false`, missing any required envelope or command-required field makes the response schema-invalid.
- When `success=true`, missing any required envelope or command-required field makes the response schema-invalid.
- Extra/unknown fields MUST NOT cause rejection; consumers ignore fields they do not care about.

Protocol requirement:

- If `success=false`, producers MUST include a non-empty `message`. If any required field for the command or envelope is missing, the consumer may treat the response as schema-invalid. Extra/unknown fields are allowed; consumers ignore them.

Notes:

- Responses MAY include additional command-specific fields beyond the envelope fields listed above.
- For failure responses, `message` SHOULD explain the failure in human-readable form.
- `message` is display-only and MUST NOT be parsed for program logic or feature detection.

### 4.3 Events

Events MUST contain:

| Field | Type | Description |
|---|---:|---|
| `type` | string | Always `"event"` |
| `event` | string | Event name |

---

## 5. Error model

### 5.1 Failure responses

For any response with `success=false`:

- `message` MUST be present (non-empty string).

`message` is a human-readable description intended to be shown to the user (e.g., in VS Output window).
It is not a machine-parseable field and has no required format beyond being a non-empty string.

### 5.2 Examples of failure `message`

- `"unsupported"`
- `"invalid state: program exited"`
- `"file not found: C:/repo/app/Missing.cs"`
- `"invalid arguments: missing line"`

---

## 6. Commands

All commands are `type="request"` messages emitted by VSX. Responses (when required) are single `type="response"` lines from the remote runtime. Optional features MAY be omitted entirely (see §6.5).

### 6.1 Command summary

| Command | Category | Response policy | Expected events / notes |
|---|---|---|---|
| `start` | Session | Respond with `success` (`true/false`). | No specific event required; VSX typically follows with `continue`/inspection commands. |
| `ready` | Session | **No response.** Fire-and-forget handshake. | Remote MAY treat it as "VSX is ready"; all initial breakpoint state has been sent and execution can formally begin. |
| `stop` | Session | **No response.** VSX disconnects immediately. | Remote MAY emit `program_exited` later if the debuggee quits for its own reasons. |
| `continue` | Run-control | Respond with `success`. | Emit `event=continued` with the resumed `threadId`. |
| `step` | Run-control | Respond with `success`. | Emit `event=stopped` with `reason="step"`. |
| `pause` (OPTIONAL) | Run-control | Respond with `success` (or `success=false` + `message:"unsupported"`). | Breaks **all** threads (no `threadId`); emit `event=stopped` with `reason="pause"` when supported. |
| `set_breakpoint` | Breakpoint mgmt | **No response.** Fire-and-forget. | Reflect changes via `breakpoint_changed` events. Emit `event=output` for failures. |
| `remove_breakpoint` | Breakpoint mgmt | **No response.** Fire-and-forget. | Emit `event=output` (e.g., warn) when removal fails. |
| `get_threads` | Inspection (REQUIRED) | Respond with `success` and `threads[]`. | VSX may emit follow-up inspection commands based on the payload. |
| `get_stack` | Inspection (REQUIRED) | Respond with `success`, `threadId`, and `frames[]`. | Emit `event=output` on schema violations. |
| `get_scope` | Inspection (REQUIRED) | Respond with `success`, `threadId`, `frameId`, `variables[]`. | Returns in-scope variables for a frame (only while stopped). |
| `get_property` | Inspection (REQUIRED) | Respond with `success`, `threadId`, `frameId`, `addr`, `typeId`, `start`, `count`, `size`, `properties[]`. | Expands a variable's members (only while stopped). |
| `get_evaluation` | Inspection (REQUIRED) | Respond with `success`, evaluation details. | If the result has children, VSX will issue `get_property` for the returned handle.|
| `set_variable` | Inspection (REQUIRED) | Respond with `success`, `threadId`, `frameId`, `addr`, `typeId`. | Writes a variable value (only while stopped). |

### 6.2 Session commands

Session commands manage the connection lifecycle between VSX and the remote runtime.

#### 6.2.1 `start`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | Always `"start"` .|

**Response policy**

- Respond with `success` (`true/false`).
- Include a descriptive `message` when `success=false`.

**Semantics**

- `start` SHOULD be idempotent (multiple `start` requests per session are harmless).

#### 6.2.2 `ready`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | Always `"ready"`. |

**Response policy**

- **No response.** This is a fire-and-forget notification.

**Semantics**

- Sent immediately after transport setup to signal that VSX finished its initialization and is ready to receive events.
- Remotes MAY buffer early events until `ready` arrives; if received late or multiple times, treat it as idempotent.
- After `ready`, VSX guarantees that the initial project breakpoint list has been transmitted, so the remote runtime MAY safely start or resume program execution.
- Runtimes SHOULD NOT emit a response; they MAY emit `event=output` for diagnostics as needed.

#### 6.2.3 `stop`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | Always `"stop"` .|

**Response policy**

- **No response.** VSX tears down the transport immediately after sending this request.

**Semantics**

- `stop` simply indicates that VS is leaving debug mode and will disconnect. It does **not** instruct the remote program to exit.
- Upon receiving `stop`, the remote runtime SHOULD clean up any debugging state (e.g., remove breakpoints, reset caches) and remain ready for a future connection.
- If the remote program later exits for its own reasons, emit `event=program_exited` when practical; otherwise no further messages are required after `stop`.

### 6.3 Run-control commands

Run-control commands manipulate the execution state of the debuggee after the session handshake is complete.

#### 6.3.1 `continue`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | |
| `threadId` | int | Target thread; REQUIRED. |

**Response policy**

- Respond with `success`. Provide a `message` when rejecting the request (e.g., program already exited).

**Semantics / events**

- On acceptance, emit `event=continued` with the resumed `threadId`.

#### 6.3.2 `step`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | |
| `threadId` | int | Target thread. |
| `stepKind` | string | Forwarded enum name; REQUIRED. |
| `stepUnit` | string | REQUIRED; only source-line stepping is supported. |

**Allowed values / meanings**

| Field | Values | Meaning |
|---|---|---|
| `stepKind` | `"STEP_INTO"`, `"STEP_OVER"`, `"STEP_OUT"` | Standard VS step kinds: into current/child call sites; over the current statement; out of the current frame. |
| `stepUnit` | `"STEP_LINE"` | Only source-line stepping is supported; instruction-level stepping is not implemented and MUST be rejected. |

Notes:

- Values are passthrough from VS; remotes SHOULD treat unknown or empty values as unsupported and respond `success=false` with a descriptive `message`. `stepUnit="STEP_INSTRUCTION"` is unsupported and MUST be rejected.

**Response policy**

- Respond with `success`. Reject with `success=false` + `message` when the runtime is not stopped.

**Semantics / events**

- Emit `event=stopped` with `reason="step"` once the operation completes.

#### 6.3.3 `pause` (OPTIONAL)

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | Always `"pause"`. |

**Response policy**

- If supported: respond with `success=true`.
- If unsupported: respond with `success=false` + `message:"unsupported"`.

**Semantics / events**

- The request instructs the remote runtime to break **all** threads; VSX does not provide a `threadId`.
- On success, emit `event=stopped` with `reason="pause"` for each thread that actually stopped (or at minimum one event whose `threadId` identifies the thread VS should focus). Provide the best-known `file` path and 1-based `line`; if no mapping exists, still include the fields using `file:""`, `line:0` to signal "unknown location".
- VSX may mark `pause` as unsupported after a single `success=false` response.

### 6.4 Breakpoint management

#### 6.4.1 `set_breakpoint`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | |
| `file` | string | Absolute or solution-relative path. |
| `line` | int | 1-based line number. |
| `function` | string | Function name; send "" when not applicable. |
| `functionLineOffset` | int | Offset relative to function entry; send `0` when unused. |
| `enabled` | bool | `true` or `false`. |
| `conditionType` | string | e.g., "none", "expression". |
| `condition` | string | Condition text (send "" when none). |

**Response policy**

- **No response.** The remote MUST NOT send a `type=response` for this command.
- If the command is ignored entirely (no response, no `output`), VSX assumes the breakpoint was accepted and will not retry automatically; any failure should therefore be surfaced via `event=output` so the user can react.
- Failures SHOULD be surfaced via `event=output` (typically `category:"error"`).

**Semantics**

- Used for both create and update scenarios.
- VSX uses (`file`,`line`) as the stable identity; remotes MUST treat that tuple as authoritative when reconciling breakpoint state.
- Emit `breakpoint_changed` or other bookkeeping events so VSX can stay in sync.

#### 6.4.2 `remove_breakpoint`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | |
| `file` | string | Canonical absolute or solution-relative path for the breakpoint. |
| `line` | int | 1-based line number for the breakpoint. |

**Response policy**

- **No response.** Errors must be surfaced via `event=output` (e.g., `category:"warn"`, `output:"remove_breakpoint failed: not found"`).
- If the remote silently ignores this command, VSX will assume the breakpoint was removed and will not resend, so emit an `output` diagnostic for any failure/mismatch.

**Semantics**

- Requests identify breakpoints by (`file`,`line`). If no matching breakpoint exists the remote SHOULD emit a warning via `event=output` (and remain otherwise silent).
- `file` and `line` mirror the canonical location used during `set_breakpoint`; VSX includes them so the runtime can double-check mismatched state or tolerate race conditions when metadata is stale.
- Because VSX may reconcile race conditions between `remove_breakpoint` and in-flight break events by falling back to file/line matching, **every** subsequent `event=stopped(reason="breakpoint")` MUST include the canonical `file` and 1-based `line`.

### 6.5 Inspection commands (REQUIRED)

Inspection commands are **required** for proper debugger functionality. If not implemented, VSX will wait indefinitely for responses, potentially causing hangs or timeouts. Runtimes MUST respond to these commands or reject them with `success=false` + `message:"unsupported"` to avoid blocking VSX.

#### 6.5.1 `get_threads`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | |

**Response policy**

- Respond with `success` plus a `threads` array.

**Response schema**

| Field | Type | Notes |
|---|---:|---|
| `threads` | array | Each entry is an object with `id:int` and `name:string`. |

**Common failures**

- Unsupported: `success=false` + `message:"unsupported"`.

#### 6.5.2 `get_stack`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | |
| `threadId` | int | Target thread. |

**Response policy / schema**

- Respond with `success`, echo `threadId`, and include `frames[]`.
- Each frame object includes: `id` (positive int), `file` (string), `line` (1-based int), `name` (string).

**Common failures**

- Thread not found: `success=false` + descriptive `message`.

#### 6.5.3 `get_scope`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | |
| `threadId` | int | Target thread (must be stopped). |
| `frameId` | int | Target frame (must be valid for the thread). |

**Response policy / schema**

- Respond with `success`, echo `threadId` and `frameId`, and include `variables[]`.
- Each entry in `variables` is a variable object (`name`, `value`, `type`, `typeId`, `size`, and `addr` fields as produced by the runtime). `size != 0` means the value is a composite/handle that can be expanded; the runtime does not inline children and relies on VSX to issue `get_property` when the user expands it in the UI. When the value is array-like, the runtime also includes `start`, `count`, `size`, and an `elements[]` array of `{ addr, name, value, type, typeId, size }` items; element `type` is the element type.

**Common failures**

- Frame not found or out of range: `success=false` + descriptive `message`.

#### 6.5.4 `get_property`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | |
| `threadId` | int | Target thread (must be stopped). |
| `frameId` | int | Target frame (must be valid for the thread). |
| `addr` | int64 | Address/handle of the variable to expand. |
| `typeId` | int | AngelScript type id for the variable. |
| `start` | int | Zero-based start index for paging; only meaningful when pulling arrays. |
| `count` | int | Page size; `count<=0` returns all children. |

**Response policy / schema**

- Respond with `success`, echo `threadId`, `frameId`, `addr`, `typeId`, `start`, `count`, `size`, and include `properties[]`.
- `size` represents the total number of elements/members: for arrays, it's the array size; for objects, it's the number of properties. MUST be >= 0 (unknown sizes are not allowed in `get_property` responses).
- `count` is the actual number of elements returned in `properties[]`: for objects, `count == size`; for arrays, `count` is the number actually pulled (may be less than requested if at end).
- `start` is echoed from the request; only meaningful for arrays.
- `properties` is an array of objects. For object properties, each object is a full variable object with `name`, `value`, `type`, `typeId`, `size`, and `addr` fields. For array elements, each object has `addr`, `name`, `value`, `type`, `typeId`, and `size` fields. To expand an individual element, send another `get_property` request using the element's `addr` and `typeId`.

**Common failures**

- Invalid frame or state: `success=false` + descriptive `message`.
- Invalid variable address/type: `success=false` + descriptive `message`.
- Invalid `start` or `count` values: `success=false` + `message` (e.g., 'invalid start index').

#### 6.5.5 `get_evaluation`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | |
| `threadId` | int | |
| `frameId` | int | |
| `expression` | string | |

**Response policy / schema**

- Respond with `success` plus: `threadId`, `frameId`, `expression`, `result` (single variable object; same shape as `get_scope` entries).

**Common failures**

- Evaluation error: `success=false` + descriptive `message`.

For `get_property`, `threadId` and `frameId` MUST be present. See §8.3 for remote-issued id validity rules; consumers MUST NOT reject solely based on numeric sign.

#### 6.5.6 `set_variable`

**Request fields**

| Field | Type | Notes |
|---|---:|---|
| `command` | string | |
| `threadId` | int | Target thread (must be stopped). |
| `frameId` | int | Target frame (must be valid for the thread). |
| `addr` | int64 | Address/handle of the variable to modify. |
| `typeId` | int | AngelScript type id for the variable. |
| `value` | string | New value (string form, parsed per type id). |

**Response policy**

- Respond with `success`, echo `threadId`, `frameId`, `addr`, `typeId`. When `success=false`, include a non-empty `message` explaining the failure (e.g., invalid frame, unsupported type, not stopped).
- Reject with `success=false` + `message` when the variable cannot be set (e.g., invalid frame, unsupported type, not stopped).

## 7. Events

Events are `type="event"` JSON lines emitted asynchronously by the remote runtime.

### 7.1 Event summary

| Event | Purpose | Key fields | Notes |
|---|---|---|---|
| `stopped` | Communicates a transition into break mode. | `threadId`, `reason`, optional source info. | Used for breakpoints, steps, pauses, and exceptions. |
| `continued` | Indicates the program resumed execution. | `threadId`. | Typically follows `continue` or `step` acknowledgements. |
| `program_exited` | Signals that the debuggee ended. | Optional `exitCode`. | VSX ends the session when observed. |
| `output` | Streams textual output or warnings/errors. | `output`, optional `category`. | Recommended for surfacing breakpoint failures. |
| `thread_started` / `thread_exited` | Thread lifetime notifications. | `threadId`. | Helps VSX keep the thread list fresh. |

### 7.2 `stopped`

**Fields**

| Field | Type | Notes |
|---|---:|---|
| `event` | string | Always "stopped". |
| `reason` | string | One of "breakpoint", "step", "pause", "exception". |
| `threadId` | int | Thread identifier; MUST be present (see §8.3 for validity). |

**Additional fields**

| Field | Type | Notes |
|---|---:|---|
| `file` | string | REQUIRED. Send the best-known path or "" if unknown. |
| `line` | int | REQUIRED. Send the best-known value or 0 if unknown. |
| `exceptionName` | string | Provide **only** when `reason="exception"`. |
| `exceptionMessage` | string | Provide **only** when `reason="exception"`. |

**Consumer notes**

- If `threadId` is missing, the event is schema-invalid; consumers MAY ignore it.
- When `reason="breakpoint"`, VSX uses the supplied `file`/`line` tuple as the breakpoint identity, so remotes MUST always include both fields.
- For `reason="step"` / `"pause"`, include the common fields (`threadId`, `file`, `line`) so VS can navigate to the current source location.
- Exception stops (`reason="exception"`) must include `exceptionName` and `exceptionMessage` in addition to the common fields (see §10.9.4).

### 7.3 `continued`

**Fields**

| Field | Type | Notes |
|---|---:|---|
| `event` | string | Always `"continued"`. |
| `threadId` | int | Thread that resumed; MUST be present (see §8.3 for validity). |

### 7.4 `program_exited`

**Fields**

| Field | Type | Notes |
|---|---:|---|
| `event` | string | Always "program_exited". |
| `exitCode` | int | Optional exit code; send `0` if unknown. |

**Notes**

- Emit this event whenever the remote program actually ends execution so the consumer can surface the result. It is independent from the `stop` command.

### 7.5 `output`

**Fields**

| Field | Type | Notes |
|---|---:|---|
| `event` | string | Always "output". |
| `output` | string | Display text. |
| `category` | string | Optional bucket (e.g., "stdout", "stderr", "info", "warn", "error"). |

**Notes**

- Preserve newline characters inside `output` if the text spans multiple lines; VSX renders them verbatim.
- `category` helps VSX route the text (Output window pane, breakpoint warning surface, etc.). When omitted, VSX treats it as general informational output.

### 7.6 `thread_started` / `thread_exited`

**Fields**

| Field | Type | Notes |
|---|---:|---|
| `event` | string | Either `"thread_started"` or `"thread_exited"`. |
| `threadId` | int | Thread identifier. |

**Notes**

## 8. Data conventions

### 8.1 Paths (`file`)

- `file` is a string path.
- Paths are interpreted in a Windows-centric way.
- Consumers compare paths case-insensitively.
- Producers MAY use either `\\` or `/` as separators; consumers MUST accept both.
- Producers MUST send either absolute paths or paths relative to the solution subdirectory (not general relative paths like `..`). Consumers MUST normalize separators and resolve subdirectory paths within the active solution. General relative paths (e.g., `..`) are NOT allowed.

Resolution behavior:

- VSX resolves incoming `file` values by normalizing separators and performing a case-insensitive match within the active solution.

### 8.2 Line numbers (`line`)

- All `line` fields are **1-based**.

### 8.3 Ids

`threadId` and `frameId` are **opaque identifiers issued by the remote runtime**. Their numeric values MAY be any integer (negative/zero/positive). Consumers MUST treat them as tokens and MUST NOT derive meaning from numeric ordering or sign.

Validity rules:

- `threadId`: MUST refer to a currently known thread in the remote runtime. Remotes allocate ids and may reuse them after a thread exits.
- `frameId`: MUST refer to a valid frame for the specified `threadId` in the current stop.

When an id is not valid in the current state, the remote MUST reject the request with `success=false` and a descriptive `message` (e.g., `"thread not found"`, `"frame not found"`).

### 8.4 Required-field policy and defaults

Unless explicitly stated otherwise in a command/event schema:

- Any field listed in a schema table is **required** and MUST be present in the JSON.
- Producers MUST NOT omit a field to indicate “not applicable”. Instead they MUST send an explicit default value.
- If a schema explicitly marks a field as OPTIONAL, producers MAY omit it.
- Extra/unknown fields MUST be tolerated by consumers; they only process fields of interest.

Default values used by this document when a concept is not applicable:

- `int`: `0`
- `bool`: `false`
- `string`: `""`
- `array`: `[]`
- `object`: `{}`

### 8.5 Size field semantics

The `size` field indicates the expandability and size of a variable:

- `size == 0`: The variable is not expandable (e.g., primitive types or empty collections). No further expansion is needed.
- `size > 0`: The variable is expandable and has a known number of children/elements (e.g., arrays with known length).
- `size < 0`: The variable is expandable but the number of children/elements is unknown; use `get_property` to retrieve the actual size when expanding. This value is allowed in `get_scope` and `get_evaluation` responses, but NOT in `get_property` responses, where `size` MUST be >= 0.

---

## 9. Ordering, state, and resilience

### 9.1 Conceptual remote state

The remote runtime is conceptually in one of:

- `running`
- `stopped`
- `exited`

State transitions are communicated via events:

- `running` → `stopped`: `event=stopped`
- `stopped` → `running`: `event=continued`
- `any → exited`: `event=program_exited`

### 9.2 Run-control serialization (consumer behavior)

The VSX-side consumer serializes **run-control** commands:

- Run-control commands are: `pause`, `continue`, `step`.
- At most **one** run-control command is considered in-flight at a time.
- The in-flight gate is cleared when the consumer observes:
  - `event=stopped`, or
  - `event=continued`, or
  - `event=program_exited`, or
  - a `response` for the in-flight run-control command.

Guidance for remote runtimes:

- Remotes SHOULD emit the corresponding state event promptly once a run-control request is accepted.

### 9.2.1 Run-control state matrix (recommended)

This section clarifies how the remote runtime SHOULD behave when receiving run-control commands in different remote states.

Remote states:

- `running`
- `stopped`
- `exited`

Notes:

- `success:true` in a response means the request was accepted (or at least not rejected immediately). It does not, by itself, imply a state transition has already occurred.
- State transitions MUST be represented by events (`continued`, `stopped`, `program_exited`).
- `stop` is special in this project: it ends the debug session and VSX will disconnect immediately; the remote process MAY keep running.

| Command | Allowed when `running` | Allowed when `stopped` | Allowed when `exited` | If accepted | If rejected | Consumer notes |
|---|---|---|---|---|---|---|
| `continue` | yes (idempotent) | yes | no | respond `success:true`, then emit `event=continued` | respond `success:false` + `message` | consumer clears run-control in-flight on response or `continued` |
| `step` | no | yes | no | respond `success:true`, then emit `event=stopped(reason=step)` | respond `success:false` + `message` | consumer clears run-control in-flight on response or `stopped` |
| `pause` (optional) | yes | yes (idempotent) | no | respond `success:true`, then emit `event=stopped(reason=pause)` | respond `success=false` + `message` | consumer may treat any `pause` response with `success:false` as unsupported for the session |
| `stop` | yes | yes | yes | (no remote messages required) | n/a | VSX disconnects immediately; do not rely on sending any response/event |

### 9.3 Required feature behavior

Inspection commands are **required** features. Runtimes MUST respond to these commands or reject them with `success=false` + `message:"unsupported"` to prevent VSX from hanging while waiting for responses.

Recommended behavior for unsupported inspection commands:

- Prefer responding with `success=false` and `message:"unsupported"` so the consumer can deterministically disable the feature.
- Alternatively, a remote MAY ignore an unsupported inspection request (send no response). This is supported for backwards compatibility but may cause UI timeouts/retries.

### 9.4 Responses vs events (implementation guidance)

This protocol uses both request/response and asynchronous events:

- A `response` answers a specific `request` (via `requestSeq`) and indicates whether the request was accepted (`success:true`) or rejected (`success:false`),
- An `event` represents a state transition or asynchronous notification (e.g., `continued`, `stopped`, `output`).

Guidance:

- For run-control commands (`continue`, `step`, `pause`), the remote SHOULD send a `response` promptly and then emit the corresponding state event when it actually occurs.
- Do not encode state transitions inside `response` messages. The consumer relies on `event=continued/stopped/program_exited` to drive its state machine.
- `stop` ends the VSX debug session and causes immediate disconnect. The remote SHOULD NOT send any messages in response to `stop`.
- A transport disconnect ends the debug session but does not imply the remote program exited. Only `event=program_exited` indicates remote exit.

### 9.5 Forward-compatibility rules

To preserve compatibility as the protocol evolves:

- Consumers SHOULD ignore unknown JSON object fields.
- If a consumer receives an unknown `command` in a `type=request`, it SHOULD respond with `success=false` and a non-empty `message` (e.g., `"unsupported"`).
- If a consumer receives an unknown `event` in a `type=event`, it SHOULD ignore it.

### 9.6 Timeouts, retries, and no-response behavior (NOT SUPPORTED)

This protocol does not define any required timeout, retry, or retransmission behavior.

- Producers SHOULD respond promptly, but consumers MUST NOT rely on retries for correctness.
- If a request receives no response (e.g., due to transport issues or a non-responsive remote), the intended mitigation is to end the debug session and start debugging again.

---

## 10. Examples

All examples are single JSON lines.

### 10.1 Start

`{ "type":"request", "seq": 1, "command":"start" }`

`{ "type":"response", "requestSeq": 1, "command":"start", "success": true }`

### 10.1.1 Start failed (failure response example)

Example: start fails due to internal error in the remote runtime:

`{ "type":"request", "seq": 16, "command":"start" }`

`{ "type":"response", "requestSeq": 16, "command":"start", "success": false, "message": "internal error" }`

### 10.2 Ready handshake (no response)

`{ "type":"request", "seq": 6, "command":"ready" }`

(transport stays open; remote begins emitting queued events once ready is observed)

### 10.3 Stop (no response required)

Example: stop request followed by VSX closing the transport; no remote messages are required and the remote program may keep running while the runtime resets its debug state and waits for a future connection:

`{ "type":"request", "seq": 18, "command":"stop" }`

(transport disconnects)

### 10.4 Continue

`{ "type":"request", "seq": 2, "command":"continue", "threadId": 1 }`

`{ "type":"response", "requestSeq": 2, "command":"continue", "success": true }`

`{ "type":"event", "event":"continued", "threadId": 1 }`

### 10.4.1 Continue failed (failure response example)

Example: continue fails because the program has already exited:

`{ "type":"request", "seq": 17, "command":"continue", "threadId": 1 }`

`{ "type":"response", "requestSeq": 17, "command":"continue", "success": false, "message": "invalid state: program exited" }`

### 10.5 Step

`{ "type":"request", "seq": 3, "command":"step", "threadId": 1, "stepKind":"STEP_INTO", "stepUnit":"STEP_LINE" }`

`{ "type":"response", "requestSeq": 3, "command":"step", "success": true }`

`{ "type":"event", "event":"stopped", "reason":"step", "threadId": 1, "file":"C:/repo/app/Program.cs", "line": 13 }`

### 10.5.1 Step rejected (failure response example)

`{ "type":"request", "seq": 5, "command":"step", "threadId": 1, "stepKind":"STEP_INTO", "stepUnit":"STEP_LINE" }`

`{ "type":"response", "requestSeq": 5, "command":"step", "success": false, "message": "invalid state: not stopped" }`

### 10.6 Pause unsupported

`{ "type":"request", "seq": 4, "command":"pause" }`

`{ "type":"response", "requestSeq": 4, "command":"pause", "success": false, "message": "unsupported" }`

### 10.6.1 Pause success (supported runtime)

`{ "type":"request", "seq": 19, "command":"pause" }`

`{ "type":"response", "requestSeq": 19, "command":"pause", "success": true }`

`{ "type":"event", "event":"stopped", "reason":"pause", "threadId": 1, "file":"C:/repo/app/Program.cs", "line": 25 }`

`{ "type":"event", "event":"stopped", "reason":"pause", "threadId": 7, "file":"C:/repo/app/Worker.cs", "line": 42 }`

### 10.7 Program exited event

`{ "type":"event", "event":"program_exited", "exitCode": 0 }`

### 10.8 Thread lifetime events

`{ "type":"event", "event":"thread_started", "threadId": 11 }`

`{ "type":"event", "event":"thread_exited", "threadId": 11 }`

### 10.9 Breakpoint hit

`{ "type":"event", "event":"stopped", "reason":"breakpoint", "threadId": 1, "file":"C:/repo/app/Program.cs", "line": 12 }`

#### 10.9.1 set_breakpoint success (create + update examples)

Example: set a new breakpoint (create):

`{ "type":"request", "seq": 27, "command":"set_breakpoint", "file":"C:/repo/app/Program.cs", "line": 12, "function":"", "functionLineOffset": 0, "enabled": true, "conditionType":"none", "condition":"" }`

Example: update the same breakpoint (e.g., add a condition):

`{ "type":"request", "seq": 28, "command":"set_breakpoint", "file":"C:/repo/app/Program.cs", "line": 12, "function":"", "functionLineOffset": 0, "enabled": true, "conditionType":"expression", "condition":"i > 10" }`

Example: set a new breakpoint (create) using a solution subdirectory path:

`{ "type":"request", "seq": 27, "command":"set_breakpoint", "file":"src/Game/Player.as", "line": 12, "function":"", "functionLineOffset": 0, "enabled": true, "conditionType":"none", "condition":"" }`

Example: update the same breakpoint (e.g., add a condition) using a solution subdirectory path:

`{ "type":"request", "seq": 28, "command":"set_breakpoint", "file":"src/Game/Player.as", "line": 12, "function":"", "functionLineOffset": 0, "enabled": true, "conditionType":"expression", "condition":"i > 10" }`

#### 10.9.2 set_breakpoint failed (failure surface example)

If the remote cannot honor a request it SHOULD emit an `output` event rather than a response:

`{ "type":"request", "seq": 21, "command":"set_breakpoint", "file":"C:/repo/app/Missing.cs", "line": 10, "function":"", "functionLineOffset": 0, "enabled": false, "conditionType":"none", "condition":"" }`

`{ "type":"event", "event":"output", "category":"error", "output":"set_breakpoint failed: file not found C:/repo/app/Missing.cs" }`

#### 10.9.3 remove_breakpoint failed (failure surface example)

Example: breakpoint location does not exist:

`{ "type":"request", "seq": 20, "command":"remove_breakpoint", "file":"C:/repo/app/Program.cs", "line": 12 }`

`{ "type":"event", "event":"output", "category":"warn", "output":"remove_breakpoint failed: not found (C:/repo/app/Program.cs:12)" }`

#### 10.9.4 Exception stop example

Example: runtime reports an unhandled exception and surfaces details in both the `stopped` event and an `output` line.

`{ "type":"event", "event":"stopped", "reason":"exception", "threadId": 7, "file":"C:/repo/app/Worker.cs", "line": 58, "exceptionName":"System.NullReferenceException", "exceptionMessage":"Object reference not set to an instance of an object." }`

`{ "type":"event", "event":"output", "category":"error", "output":"Unhandled exception in Worker.Run: System.NullReferenceException" }`

### 10.10 Inspection command walkthrough

The following JSON lines show a typical inspection flow once the program is stopped.

#### 10.10.1 Enumerate threads

`{ "type":"request", "seq": 60, "command":"get_threads" }`

`{ "type":"response", "requestSeq": 60, "command":"get_threads", "success": true, "threads": [ { "id": 1, "name": "Main Thread" }, { "id": 7, "name": "Worker #1" } ] }`

#### 10.10.2 Fetch stack and scope

`{ "type":"request", "seq": 61, "command":"get_stack", "threadId": 7 }`

`{ "type":"response", "requestSeq": 61, "command":"get_stack", "success": true, "threadId": 7, "frames": [ { "id": 0, "name": "Worker.Run", "file": "C:/repo/app/Worker.cs", "line": 42 }, { "id": 1, "name": "Task.InnerInvoke", "file": "C:/repo/framework/Task.cs", "line": 119 } ] }`

`{ "type":"request", "seq": 62, "command":"get_scope", "threadId": 7, "frameId": 0 }`

`{ "type":"response", "requestSeq": 62, "command":"get_scope", "success": true, "threadId": 7, "frameId": 0, "variables": [ { "addr": 1407329216, "name": "i", "value": "11", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329250, "name": "task", "value": "{...}", "type": "Task@", "typeId": 4194307, "size": -1 } ] }`

`{ "type":"request", "seq": 73, "command":"get_scope", "threadId": 7, "frameId": 0 }`

`{ "type":"response", "requestSeq": 73, "command":"get_scope", "success": true, "threadId": 7, "frameId": 0, "variables": [ { "addr": 1407329400, "name": "buffer", "value": "[...]", "type": "array<int>", "typeId": 2002, "size": 5, "start": 0, "count": 3, "elements": [ { "addr": 1407329401, "name": "buffer[0]", "value": "1", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329402, "name": "buffer[1]", "value": "2", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329403, "name": "buffer[2]", "value": "3", "type": "int", "typeId": 2, "size": 0 } ] } ] }`

`{ "type":"request", "seq": 67, "command":"get_stack", "threadId": 99 }`

`{ "type":"response", "requestSeq": 67, "command":"get_stack", "success": false, "message": "thread not found: 99" }`

`{ "type":"request", "seq": 69, "command":"get_scope", "threadId": 7, "frameId": 99 }`

`{ "type":"response", "requestSeq": 69, "command":"get_scope", "success": false, "message": "Invalid frameId=99" }`

#### 10.10.3 Expand properties

`{ "type":"request", "seq": 63, "command":"get_property", "threadId": 7, "frameId": 0, "addr": 1407329250, "typeId": 4194307, "start": 0, "count": 0 }`

`{ "type":"response", "requestSeq": 63, "command":"get_property", "success": true, "threadId": 7, "frameId": 0, "addr": 1407329250, "typeId": 4194307, "start": 0, "count": 2, "size": 2, "properties": [ { "addr": 1407329300, "name": "Status", "value": "RanToCompletion", "type": "TaskStatus", "typeId": 1001, "size": 0 }, { "addr": 1407329312, "name": "Result", "value": "42", "type": "int", "typeId": 2, "size": 0 } ] }`

`{ "type":"request", "seq": 64, "command":"get_property", "threadId": 7, "frameId": 0, "addr": 1407329250, "typeId": 4194307, "start": 0, "count": 0 }`

`{ "type":"response", "requestSeq": 64, "command":"get_property", "success": false, "threadId": 7, "frameId": 0, "addr": 123, "typeId": 4194307, "message": "invalid varAddr: 123" }`

#### 10.10.3.1 Expand array properties

Example: expanding an array variable to retrieve its elements:

`{ "type":"request", "seq": 74, "command":"get_property", "threadId": 7, "frameId": 0, "addr": 1407329400, "typeId": 2002, "start": 0, "count": 5 }`

`{ "type":"response", "requestSeq": 74, "command":"get_property", "success": true, "threadId": 7, "frameId": 0, "addr": 1407329400, "typeId": 2002, "start": 0, "count": 5, "size": 5, "properties": [ { "addr": 1407329401, "name": "buffer[0]", "value": "1", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329402, "name": "buffer[1]", "value": "2", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329403, "name": "buffer[2]", "value": "3", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329404, "name": "buffer[3]", "value": "4", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329405, "name": "buffer[4]", "value": "5", "type": "int", "typeId": 2, "size": 0 } ] }`

#### 10.10.3.2 Expand array properties with paging

Example: expanding a large array with paging to retrieve elements in chunks:

`{ "type":"request", "seq": 75, "command":"get_property", "threadId": 7, "frameId": 0, "addr": 1407329400, "typeId": 2002, "start": 0, "count": 3 }`

`{ "type":"response", "requestSeq": 75, "command":"get_property", "success": true, "threadId": 7, "frameId": 0, "addr": 1407329400, "typeId": 2002, "start": 0, "count": 3, "size": 10, "properties": [ { "addr": 1407329401, "name": "buffer[0]", "value": "1", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329402, "name": "buffer[1]", "value": "2", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329403, "name": "buffer[2]", "value": "3", "type": "int", "typeId": 2, "size": 0 } ] }`

`{ "type":"request", "seq": 76, "command":"get_property", "threadId": 7, "frameId": 0, "addr": 1407329400, "typeId": 2002, "start": 3, "count": 3 }`

`{ "type":"response", "requestSeq": 76, "command":"get_property", "success": true, "threadId": 7, "frameId": 0, "addr": 1407329400, "typeId": 2002, "start": 3, "count": 3, "size": 10, "properties": [ { "addr": 1407329404, "name": "buffer[3]", "value": "4", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329405, "name": "buffer[4]", "value": "5", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329406, "name": "buffer[5]", "value": "6", "type": "int", "typeId": 2, "size": 0 } ] }`

#### 10.10.3.3 Handling unknown size (size < 0)

Example: expanding a variable initially reported with unknown size (`size < 0`), then retrieving the actual size via `get_property`:

First, from `get_scope` or `get_evaluation`:

`{ "type":"response", "requestSeq": 62, "command":"get_scope", "success": true, "threadId": 7, "frameId": 0, "variables": [ { "addr": 1407329250, "name": "task", "value": "{...}", "type": "Task@", "typeId": 4194307, "size": -1 } ] }`

Then, `get_property` request:

`{ "type":"request", "seq": 77, "command":"get_property", "threadId": 7, "frameId": 0, "addr": 1407329250, "typeId": 4194307, "start": 0, "count": 0 }`

Response with actual size:

`{ "type":"response", "requestSeq": 77, "command":"get_property", "success": true, "threadId": 7, "frameId": 0, "addr": 1407329250, "typeId": 4194307, "start": 0, "count": 2, "size": 2, "properties": [ { "addr": 1407329300, "name": "Status", "value": "RanToCompletion", "type": "TaskStatus", "typeId": 1001, "size": 0 }, { "addr": 1407329312, "name": "Result", "value": "42", "type": "int", "typeId": 2, "size": 0 } ] }`

#### 10.10.4 Set a variable

`{ "type":"request", "seq": 70, "command":"set_variable", "threadId": 7, "frameId": 0, "addr": 1407329216, "typeId": 2, "value": "12" }`

`{ "type":"response", "requestSeq": 70, "command":"set_variable", "success": true, "threadId": 7, "frameId": 0, "addr": 1407329216, "typeId": 2 }`

`{ "type":"request", "seq": 71, "command":"set_variable", "threadId": 7, "frameId": 99, "addr": 1407329216, "typeId": 2, "value": "12" }`

`{ "type":"response", "requestSeq": 71, "command":"set_variable", "success": false, "message": "Invalid frameId=99" }`

#### 10.10.5 Get Evaluation an expression

`{ "type":"request", "seq": 65, "command":"get_evaluation", "threadId": 7, "frameId": 0, "expression": "task.Status" }`

`{ "type":"response", "requestSeq": 65, "command":"get_evaluation", "success": true, "threadId": 7, "frameId": 0, "expression": "task.Status", "result": { "addr": 1407329216, "name": "task.Status", "value": "RanToCompletion", "type": "TaskStatus", "typeId": 1001, "size": 0 } }`

`{ "type":"request", "seq": 66, "command":"get_evaluation", "threadId": 7, "frameId": 0, "expression": "buffer" }`

`{ "type":"response", "requestSeq": 66, "command":"get_evaluation", "success": true, "threadId": 7, "frameId": 0, "expression": "buffer", "result": { "addr": 1407329500, "name": "buffer", "value": "[...]", "type": "array<int>", "typeId": 2002, "size": 5, "start": 0, "count": 3, "elements": [ { "addr": 1407329501, "name": "buffer[0]", "value": "10", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329502, "name": "buffer[1]", "value": "20", "type": "int", "typeId": 2, "size": 0 }, { "addr": 1407329503, "name": "buffer[2]", "value": "30", "type": "int", "typeId": 2, "size": 0 } ] } }`

`{ "type":"request", "seq": 72, "command":"get_evaluation", "threadId": 7, "frameId": 0, "expression": "buffer[0]" }`

`{ "type":"response", "requestSeq": 72, "command":"get_evaluation", "success": false, "message": "buffer is null" }`


---

## 11. Conformance checklist

### 11.1 General

- [ ] All JSON messages are well-formed and valid UTF-8.
- [ ] Conformance to protocol rules on JSON object fields: unknown fields are ignored.
- [ ] Conformance to protocol rules on request/response: no unsolicited responses.
- [ ] Conformance to protocol rules on events: no missing or extra fields.

### 11.2 Run-control

- [ ] `start` waits for `event=continued` before accepting new run-control requests.
- [ ] `stop` terminates the session immediately; no response or events.
- [ ] `continue` in `stopped` state is idempotent; emits `event=continued` once.
- [ ] `step` in `stopped` state is accepted and emits `event=stopped(reason=step)`.
- [ ] `pause` in `running` or `stopped` state is accepted and emits `event=stopped(reason=pause)`.
- [ ] `ready` is accepted without a response; remotes only start streaming buffered events after they see it.

### 11.3 Breakpoints

- [ ] `set_breakpoint` accepts `file` + `line`; any failures are emitted via `event=output` (no response required).
- [ ] `remove_breakpoint` accepts `file` + `line`; failures are emitted via `event=output` (no response required).
- [ ] Breakpoint stop emits `event=stopped(reason=breakpoint)` with canonical `file` and `line`.

### 11.4 Inspection (REQUIRED)

- [ ] The following inspection commands are supported and behave as specified:
- [ ] `get_threads`
- [ ] `get_stack`
- [ ] `get_scope`
- [ ] `get_property`
- [ ] `get_evaluation`
- [ ] `set_variable`

## 12. Implementation Guidelines

### 12.1 Performance considerations
- Respond to inspection commands promptly to avoid UI timeouts (recommended < 500ms for simple queries).
- For large data structures, use paging (`start` and `count`) to limit response size and prevent UI lag.
- Cache variable metadata where possible, but invalidate on state changes (e.g., after `continue` or `step`).

### 12.2 Error handling best practices
- Always include a descriptive `message` in failure responses.
- For invalid `threadId` or `frameId`, respond with `success=false` and a clear message (e.g., "thread not found").
- Log internal errors but surface user-friendly messages (e.g., avoid exposing stack traces).

### 12.3 State management
- Track program state accurately and reject commands in invalid states (e.g., `step` when running).
- Emit events immediately after state transitions to keep VSX synchronized.
- Handle reconnections by resetting state and waiting for `start`/`ready`.

### 12.4 Compatibility and extensibility
- Ignore unknown fields in requests to allow protocol evolution.
- Use `event=output` for diagnostics or warnings.
- Test against the conformance checklist (§11) to ensure full compatibility.
