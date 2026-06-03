# Index Rulebook

This document describes the decision process for reconciling a file with the
index.

## Index structure

File reconciliation in FSDB works as follows:

- Collect data about the current state of the file and the index.
- Run the decision algorithm, which uses the available data to determine the actions that will bring the file representation in the index up to date.
- Execute those actions.

Data is collected using the following methods:

| Action | Result | Meaning |
| --- | --- | --- |
| `GetFingerprint` | `{PresentFile, Fingerprint}` | Observes whether the file exists and, when it exists, returns the current file fingerprint. |
| `GetState` | `{PresentState, Fingerprint, Id, Error}` | Reads indexed state for the path from the current index scope. |
| `ReadFile` | `{PresentFile, Fingerprint, Id, Error}` | Reads the file. |

The index contains all disk files whose ids are known. A disk file whose id cannot
be observed is not represented in the index.

Each file is stored in the index as
`FileIndexState(Projection, Fingerprint, Error)`. When a file is read
successfully, the indexed file represents a database record projection. When the
file cannot be read, the indexed file represents an error state.

The decision algorithm changes indexed state through decision actions:

- `Delete` - delete indexed state.
- `UpsertRecord` - add or update indexed valid file state.
- `UpsertError` - add or update indexed file error.

`UpsertError` moves the indexed file to error state: the previous projection and
fingerprint remain, and `Error` is set. To recover the indexed file from error
state, `UpsertRecord` must be applied, even when the id and fingerprint did not
change, because it clears `Error` and restores record state.

## Reconciliation Decision Tree

This section derives the complete decision tree for file reconciliation. The
full decision space requires reading the file, but some decisions can be made
before that read. Because of that, the reconciliation algorithm is split into two
phases:

1. The situation before reading the file.
2. The situation after reading the file.

Together, these phases form one long decision chain: Phase 1 either returns a
decision directly or reads the file and continues into Phase 2.

### Codes

Meanings:

| Code | Name | Meaning |
| --- | --- | --- |
| `PF` | Present File | The observed file exists on disk. |
| `PS` | Present State | The path has indexed state. |
| `EF` | Error File | The observed file is an error instead of a record. |
| `ES` | Error State | The indexed state is an error instead of record-like state. |
| `RI` | Relation Id | Relation between observed id and indexed id. |
| `RF` | Relation Fingerprint | Relation between observed fingerprint and indexed fingerprint. |
| `RE` | Relation Error | Relation between observed error and indexed error. |

Sets:

```text
P = {PF, PS}
E = {EF, ES}
R = {RI, RF, RE}
U = {PF, PS, EF, ES, RI, RF, RE}
```

Notation:

- `0` = false.
- `1` = true.
- `{}` = empty set.
- `Disabled` = decision inputs unavailable to the current row.
- `Disables` = decision inputs disabled by the current row values.
- `Available` = decision inputs available to the current row.
- In table rows, `Available` is calculated after applying `Disables`.

Formula:

```text
Available = U - Fixed - Disabled
```

### Availability Rules

| Rule | Values | Disables |
| --- | --- | --- |
| `A1` | `PF=0` | `{EF, RI, RF, RE}` |
| `A2` | `PS=0` | `{ES, RI, RF, RE}` |
| `A3` | `EF=1` | `{RI}` |
| `A4` | `EF=0` | `{RE}` |
| `A5` | `ES=0` | `{RE}` |

### Phase 1: Pre-Read Decision

The executable reconciliation algorithm starts with the pre-read decision. It may
decide early when the decision does not require file contents.

#### Table 1.1. Presence

```text
Fixed = {}
Available = {PF, PS, ES, RF}
```

| PF1 | PS | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| 0 | 0 | `{ES, RF}` | `{}` | `Skip` | The file and indexed state are absent before read - current. |
| 0 | 1 | `{RF}` | `{ES}` | [Table 2](#table-12-missing-file) | |
| 1 | 0 | `{ES, RF}` | `{}` | `ReadFile`, then [Phase 2](#phase-2-decision) | The file is new to the index and must be read before it can be indexed. |
| 1 | 1 | `{}` | `{ES, RF}` | [Table 3](#table-13-existing-file) | |

#### Table 1.2. Missing File

```text
Fixed = {PF=0, PS=1}
Disabled = {RF}
Available = {ES}
```

| ES | Disables | Available | Decision |
| --- | --- | --- | --- |
| 0 | `{}` | `{}` | `Delete` |
| 1 | `{}` | `{}` | `Delete` |

The file is absent on disk - delete indexed state.

#### Table 1.3. Existing File

```text
Fixed = {PF=1, PS=1}
Available = {ES, RF}
```

| ES | RF1 | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| 0 | same | `{}` | `{}` | `Skip` | The indexed state is record-like and the pre-read fingerprint matches, so the file contents do not need to be read. |
| 0 | different | `{}` | `{}` | `ReadFile`, then [Phase 2](#phase-2-post-read-decision) | The record-like indexed state may be stale. |
| 1 | same | `{}` | `{}` | `ReadFile`, then [Phase 2](#phase-2-post-read-decision) | A readable file with the same fingerprint still requires `UpsertRecord` to recover from indexed error state. |
| 1 | different | `{}` | `{}` | `ReadFile`, then [Phase 2](#phase-2-post-read-decision) | The indexed error state may be stale. |

#### Compact Decision Tree

```text
GetFingerprint → PF, file fingerprint
GetState → PS, ES, indexed state
└─ Pre-read presence
   ├─ PF1=0, PS=0 → Skip
   ├─ PF1=0, PS=1 → Delete
   ├─ PF1=1, PS=0
   │  └─ ReadFile → Phase 2
   └─ PF1=1, PS=1
      ├─ ES=0, RF=same → Skip
      └─ otherwise
         └─ ReadFile → Phase 2
```

### Phase 2: Post-Read Decision

Phase 2 starts when Phase 1 returns `ReadFile`. It attempts to read the file,
builds post-read inputs, and then delegates to the complete reconciliation
tables below.

Since `ReadFile` obtains new `{PresentFile, Fingerprint}` values, in the second phase `PF` and `RF` are different variables from the ones used in the first phase.

#### Table 2.1. Presence

```text
Fixed = {}
Disabled = {}
Available = {PF, PS, EF, ES, RI, RF, RE}
```

| PF | PS | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| 0 | 0 | `{EF, ES, RI, RF, RE}` | `{}` | `Skip` | The file and indexed state are absent - current. |
| 0 | 1 | `{EF, RI, RF, RE}` | `{ES}` | [Table 2](#table-22-missing-file) | |
| 1 | 0 | `{ES, RI, RF, RE}` | `{EF}` | [Table 3](#table-23-new-file) | |
| 1 | 1 | `{}` | `{EF, ES, RI, RF, RE}` | [Table 4](#table-24-content-kind) | |

#### Table 2.2. Missing File

```text
Fixed = {PF=0, PS=1}
Disabled = {EF, RI, RF, RE}
Available = {ES}
```

| ES | Disables | Available | Decision |
| --- | --- | --- | --- |
| 0 | `{RE}` | `{}` | `Delete` |
| 1 | `{}` | `{}` | `Delete` |

The file is absent on disk - delete indexed state.

#### Table 2.3. New File

```text
Fixed = {PF=1, PS=0}
Disabled = {ES, RI, RF, RE}
Available = {EF}
```

| EF | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- |
| 0 | `{RE}` | `{}` | `UpsertRecord` | The file is readable and absent from the index - add. |
| 1 | `{RI}` | `{}` | `Skip` | The file id is unknown - cannot index. |

#### Table 2.4. Content Kind

```text
Fixed = {PF=1, PS=1}
Disabled = {}
Available = {EF, ES, RI, RF, RE}
```

| EF | ES | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| 0 | 0 | `{RE}` | `{RI, RF}` | [Table 5](#table-25-record-state-comparison) | |
| 1 | 0 | `{RI, RE}` | `{RF}` | `UpsertError` | The indexed record state differs from the file - reconcile as error. |
| 0 | 1 | `{RE}` | `{RI, RF}` | [Table 6](#table-26-record-recovery-from-error) | |
| 1 | 1 | `{RI}` | `{RF, RE}` | [Table 7](#table-27-error-comparison) | |

#### Table 2.5. Record State Comparison

```text
Fixed = {PF=1, PS=1, EF=0, ES=0}
Disabled = {RE}
Available = {RI, RF}
```

| RI | RF | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| same | same | `{}` | `{}` | `Skip` | The indexed record matches the file - current. |
| same | different | `{}` | `{}` | `UpsertRecord` | The indexed record differs from the file - reconcile. |
| different | same | `{}` | `{}` | `Delete`, then `UpsertRecord` | The indexed id differs from the file - move relation. |
| different | different | `{}` | `{}` | `Delete`, then `UpsertRecord` | The indexed id and record differ from the file - move relation. |

#### Table 2.6. Record Recovery From Error

```text
Fixed = {PF=1, PS=1, EF=0, ES=1}
Disabled = {RE}
Available = {RI, RF}
```

| RI | RF | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| same | same | `{}` | `{}` | `UpsertRecord` | The indexed state is error but the file is readable - reconcile. |
| same | different | `{}` | `{}` | `UpsertRecord` | The indexed error state differs from the file - reconcile. |
| different | same | `{}` | `{}` | `Delete`, then `UpsertRecord` | The indexed id differs from the file - move relation. |
| different | different | `{}` | `{}` | `Delete`, then `UpsertRecord` | The indexed id and record differ from the file - move relation. |

#### Table 2.7. Error Comparison

```text
Fixed = {PF=1, PS=1, EF=1, ES=1}
Disabled = {RI}
Available = {RF, RE}
```

| RF | RE | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| same | same | `{}` | `{}` | `Skip` | The indexed error matches the file - current. |
| same | different | `{}` | `{}` | `UpsertError` | The indexed error differs from the file - reconcile. |
| different | same | `{}` | `{}` | `UpsertError` | The indexed fingerprint differs from the file - reconcile. |
| different | different | `{}` | `{}` | `UpsertError` | The indexed error and fingerprint differ from the file - reconcile. |

#### Compact Decision Tree

```text
ReadFile → PF, file fingerprint, file id, file error
Use current GetState result → PS, ES, indexed state
└─ Presence
   ├─ PF=0, PS=0 → Skip
   ├─ PF=0, PS=1 → Delete
   ├─ PF=1, PS=0
   │  ├─ EF=0 → UpsertRecord
   │  └─ EF=1 → Skip
   └─ PF=1, PS=1
      ├─ EF=0, ES=0
      │  ├─ RI=same, RF=same → Skip
      │  ├─ RI=same, RF=different → UpsertRecord
      │  └─ RI=different → Delete, then UpsertRecord
      ├─ EF=1, ES=0 → UpsertError
      ├─ EF=0, ES=1
      │  ├─ RI=same → UpsertRecord
      │  └─ RI=different → Delete, then UpsertRecord
      └─ EF=1, ES=1
         ├─ RF=same, RE=same → Skip
         └─ otherwise → UpsertError
```

## Decision Finalization

Phase 1 and Phase 2 produce a terminal decision for the current observation.
`Skip` can be finalized immediately. Any decision that changes the index must be
finalized under id locks that cover every affected id.

Required id locks:

| Decision | Required id locks |
| --- | --- |
| `Skip` | none |
| `Delete` | indexed id |
| `UpsertRecord` | read id |
| `UpsertError` | indexed id |
| `Delete`, then `UpsertRecord` | indexed id and read id |

If no id locks are held, the implementation takes the required locks and starts a
fresh decision iteration from Phase 1. The previous terminal decision is not
executed directly because the file system or index may have changed while locks
were being acquired. If a previous read result exists, the new iteration may pass
it to `ReadFile`; `ReadFile` decides whether that result can be reused.

If some id locks are already held but they do not cover the required ids, the
work is scheduled for retry. This allows user-facing operations such as
`ReadFile` to return their already-read result without blocking on a different
lock set for index synchronization.

```text
Terminal decision
├─ Skip → ExecuteDecision
└─ changes index
   ├─ held locks cover required locks → ExecuteDecision
   ├─ no id locks are held → TakeLocks, then restart from Phase 1
   └─ held locks do not cover required locks → ScheduleRetry
```
