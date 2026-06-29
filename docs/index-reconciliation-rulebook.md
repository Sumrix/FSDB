# Index Reconciliation Rulebook

This document describes the decision process for reconciling one file with the in-memory index.

## Chapter 1. Index Model

### The General Model

In FSDB, files on disk are treated as the source of truth. A user or another process may create, edit, rename, or delete a file directly on disk without going through the library. The in-memory index is only a lagging representation of the observed file state.

```text
files on disk -> reconciliation algorithm -> in-memory index
```

The reconciliation algorithm observes one path, compares the file with its indexed state, and applies the actions needed to make the index reflect that observation.

### The Index

The index contains every disk file whose id is known. A file whose id cannot be observed cannot be related to a record and is therefore not represented in the index.

The indexed file state can be viewed as:

```text
FileIndexState(RecordId, RecordProjection, FileFingerprint, FileError?)
```

`FileError` is optional. When the file is read successfully, `UpsertRecord` refreshes the projection and fingerprint and clears the error, setting the indexed file to record state. When the file is already related to an id but can no longer be read or decoded, `UpsertError` preserves the previous projection, updates the observed fingerprint, and sets the error, setting the indexed file to error state.

The index groups all paths with the same id into one `RecordIndexState`. The logical database still exposes one current record for that id.

### The Reconciliation Algorithm

At the highest level, reconciliation has three steps:

```text
collect data -> analyze data -> update the index
```

#### Collect Data

The algorithm collects observations through three operations:

| Operation | Result | Meaning |
| --- | --- | --- |
| `GetFingerprint` | `{PresentFile, Fingerprint}` | Observes whether the file exists and, when it exists, returns its current fingerprint. |
| `GetState` | `{PresentState, Fingerprint, Id, Error}` | Reads the indexed state currently associated with the path. |
| `ReadFile` | `{PresentFile, Fingerprint, Id, Error}` | Reads and decodes the file, producing its complete observed state. |

A fingerprint is a cheap file metadata snapshot used to decide whether the file may have changed. In FSDB, the fingerprint is based on the file size and the last write time. `GetFingerprint` does not read file contents, so it is expected to be much faster than `ReadFile`.

FSDB uses this fingerprint as its file change signal. If the target file system or runtime environment cannot reliably expose file size and last-write-time changes for the way files are edited, FSDB cannot reliably detect unchanged files and is not a good fit for that environment.

Each result is an observation made at a particular moment. The algorithm must assume that both the file system and the index may change between observations.

#### Analyze Data

The algorithm compares the observed file and indexed state using these inputs:

| Input | Question |
| --- | --- |
| File presence | Does the file currently exist? |
| State presence | Does the path currently have indexed state? |
| File kind | Is the observed file a record or an error? |
| State kind | Is the indexed state record-like or an error? |
| Id relation | Do the observed and indexed ids match? |
| Fingerprint relation | Do the observed and indexed fingerprints match? |
| Error relation | Do the observed and indexed errors match? |

From the available inputs, the decision algorithm produces one of these results:

| Decision | Meaning |
| --- | --- |
| `Skip` | The observed file and indexed state are already consistent, or there is nothing that can be indexed. |
| `ReadFile` | More data is required; read the file and continue analysis. |
| `Delete` | Remove the path from its indexed id. |
| `UpsertRecord` | Add or update readable record state. |
| `UpsertError` | Add or update error state for a path whose indexed id is known. |
| `Delete`, then `UpsertRecord` | Move the path from the indexed id to the id read from the file. |

#### Update the Index

`Skip` completes reconciliation without changing the index. Every other terminal decision changes the index and must be executed while holding all required id locks. The executable algorithm is defined in [Chapter 4. Algorithm Implementation](#chapter-4-algorithm-implementation).

### Why the Decision Model Is Exhaustive

The index determines what records the database exposes to its users. A wrong decision can hide a valid record, retain a deleted record, attach a file to the wrong id, or replace useful state with an error. User data is the most important part of the database, so the decision process must explicitly cover every relevant situation.

The direct approach would be one truth table with every combination of every input. With `N` independent binary inputs, that table has `2^N` rows and `N` input columns. For the seven modeled inputs in this rulebook, that starts with `2^7 = 128` rows. Such a table is difficult to review and mostly consists of duplicate or impossible situations. For example, the relation between file and indexed ids is unavailable when either side has no id.

Instead, the rulebook uses a series of connected tables. Each table fixes some inputs, disables inputs that are no longer meaningful, and delegates the remaining cases to a smaller table. These tables form a decision tree. The tree is shorter than the full truth table while still making the complete reachable decision space reviewable.

### The Read Boundary

`ReadFile` is expensive because it performs file I/O and parses the contents. Many situations can be resolved using only `GetFingerprint` and `GetState`, so the decision chain is split by the read boundary:

```text
pre-read decision tree -> ReadFile, only when required -> post-read decision tree
```

The most important hot path is an existing readable indexed file whose fingerprint has not changed. Reading file attributes for `GetFingerprint` is nearly instantaneous compared with reading and decoding the file. In that case, the pre-read tree returns `Skip` and avoids `ReadFile` entirely.

The two trees are different because they operate on different available data. Before `ReadFile`, the file id, file error, and complete post-read fingerprint are not available. After `ReadFile`, the algorithm can compare the complete observed file state with the index.

### The Lock Boundary

In a concurrent environment, every index mutation must happen under id locks that cover the affected records. The mutation must also be based on observations confirmed while those records are locked. Otherwise, one reconciliation worker could read an older file state and later overwrite a newer index state written by another worker.

The strict form of the algorithm would therefore collect data and execute the decision while holding the required id locks:

```text
Lock()
fingerprint = GetFingerprint()
state = GetState()
decision = MakePreReadDecision(fingerprint, state)
if decision is ReadFile:
    readResult = ReadFile(path)
    decision = MakePostReadDecision(readResult, state)
ExecuteDecision(decision)
```

However, the complete lock set is not always known before the file is read. The indexed state may belong to one id, while the file on disk may contain another id. A new file may have no indexed id at all. If the algorithm locks only the indexed id first, the decision may still discover that a different or additional id must be locked. A strict locked implementation would then need a second pass with the correct locks:

```text
Lock()
fingerprint = GetFingerprint()
state = GetState()
decision = MakePreReadDecision(fingerprint, state)
if decision is ReadFile:
    readResult = ReadFile(path)
    decision = MakePostReadDecision(readResult, state)

Lock()
fingerprint = GetFingerprint()
state = GetState()
decision = MakePreReadDecision(fingerprint, state)
if decision is ReadFile:
    readResult = ReadFile(path)
    decision = MakePostReadDecision(readResult, state)
ExecuteDecision(decision)
```

This first lock rarely gives a useful benefit. Most unchanged files are resolved by `Skip` before `ReadFile`, and `Skip` does not mutate the index. When a file does need to be read, the read result can be treated as speculative and checked again after the required id locks are acquired. If the fingerprint observed under the locks still matches the cached read result, the read result is still valid for the locked decision. If the fingerprint changed, the file is read again.

This avoids holding id locks during file I/O and decoding. It also keeps the hot path for already-current files lock-free. The first pass is used only to discover whether a mutation is needed and which ids it may affect. A mutating decision from the first pass is never executed directly.

The final shape of the algorithm is:

```text
without id locks:
    fingerprint = GetFingerprint()
    state = GetState()
    decision = MakePreReadDecision(fingerprint, state)
    if decision is ReadFile:
        readResult = ReadFile(path)
        decision = MakePostReadDecision(readResult, state)
    Skip -> return
    Mutation -> acquire required id locks

with required id locks:
    fingerprint = GetFingerprint()
    state = GetState()
    decision = MakePreReadDecision(fingerprint, state)
    if decision is ReadFile:
        readResult = ResolveReadFile(path, readCache, fingerprint)
        decision = MakePostReadDecision(readResult, state)
    ExecuteDecision(decision)
```

## Chapter 2. Decision Tables

This chapter derives the complete reconciliation decision tree. It formalizes which inputs are available in each situation, removes impossible combinations, and maps every reachable combination to a decision.

### Codes

Meanings:

| Code | Name | Values | Meaning |
| --- | --- | --- | --- |
| `PF` | Present File | `0`, `1` | The observed file exists on disk. |
| `PS` | Present State | `0`, `1` | The path has indexed state. |
| `EF` | Error File | `0`, `1` | The observed file is an error instead of a record. |
| `ES` | Error State | `0`, `1` | The indexed state is an error instead of record-like state. |
| `RI` | Relation Id | `same`, `different` | Relation between observed id and indexed id. |
| `RF` | Relation Fingerprint | `same`, `different` | Relation between observed fingerprint and indexed fingerprint. |
| `RE` | Relation Error | `same`, `different` | Relation between observed error and indexed error. |

Sets:

```text
U = {PF, PS, EF, ES, RI, RF, RE}
```

Notation:

- `0` = false.
- `1` = true.
- `{}` = empty set.
- `Disabled` = decision inputs unavailable to the current table.
- `Disables` = decision inputs made unavailable by the current row values.
- `Available` = decision inputs available to the current row.
- In table rows, `Available` is calculated after applying `Disables`.

Formula:

```text
Available = U - Fixed - Disabled - Disables
```

### Availability Rules

| Values | Disables |
| --- | --- |
| `PF=0` | `{EF, RI, RF, RE}` |
| `PS=0` | `{ES, RI, RF, RE}` |
| `EF=1` | `{RI}` |
| `EF=0` | `{RE}` |
| `ES=0` | `{RE}` |

### Pre-Read Decision

The executable reconciliation algorithm starts with the pre-read decision. It may decide early when the decision does not require file contents.

Before `ReadFile`, the algorithm can only observe file presence and fingerprint through `GetFingerprint`, and indexed state through `GetState`. The observed file kind and observed file id are not available yet, so `EF` and `RI` are disabled. Because `RE` compares observed and indexed errors, it is unavailable without the observed file error.

#### Table 1.1. Presence

```text
Fixed = {}
Disabled = {EF, RI, RE}
Available = {PF, PS, ES, RF}
```

| PF | PS | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| 0 | 0 | `{ES, RF}` | `{}` | `Skip` | The file and indexed state are absent before read: current. |
| 0 | 1 | `{RF}` | `{ES}` | [Table 1.2](#table-12-missing-file) | |
| 1 | 0 | `{ES, RF}` | `{}` | `ReadFile`, then [Post-Read Decision](#post-read-decision) | The file is new to the index and must be read before it can be indexed. |
| 1 | 1 | `{}` | `{ES, RF}` | [Table 1.3](#table-13-existing-file) | |

#### Table 1.2. Missing File

```text
Fixed = {PF=0, PS=1}
Disabled = {EF, RI, RF, RE}
Available = {ES}
```

| ES | Disables | Available | Decision |
| --- | --- | --- | --- |
| 0 | `{}` | `{}` | `Delete` |
| 1 | `{}` | `{}` | `Delete` |

The file is absent on disk, so its indexed state must be deleted.

#### Table 1.3. Existing File

```text
Fixed = {PF=1, PS=1}
Disabled = {EF, RI, RE}
Available = {ES, RF}
```

| ES | RF | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| 0 | same | `{}` | `{}` | `Skip` | The indexed state is record-like and the pre-read fingerprint matches, so the file contents do not need to be read. |
| 0 | different | `{}` | `{}` | `ReadFile`, then [Post-Read Decision](#post-read-decision) | The record-like indexed state may be stale. |
| 1 | same | `{}` | `{}` | `ReadFile`, then [Post-Read Decision](#post-read-decision) | A readable file with the same fingerprint still requires `UpsertRecord` to recover from indexed error state. |
| 1 | different | `{}` | `{}` | `ReadFile`, then [Post-Read Decision](#post-read-decision) | The indexed error state may be stale. |

#### Compact Pre-Read Decision Tree

```text
GetFingerprint -> PF, pre-read file fingerprint
GetState       -> PS, ES, indexed state
├─ PF=0, PS=0 -> Skip
├─ PF=0, PS=1 -> Delete
├─ PF=1, PS=0 -> ReadFile -> Post-Read Decision
└─ PF=1, PS=1
   ├─ ES=0, RF=same -> Skip
   └─ otherwise     -> ReadFile -> Post-Read Decision
```

### Post-Read Decision

The post-read decision starts when the pre-read decision returns `ReadFile`. It reads the file, builds post-read inputs, and delegates to the complete reconciliation tables below.

Because `ReadFile` produces a new `{PresentFile, Fingerprint}` observation, the post-read values of `PF` and `RF` are different observations from the values used in the pre-read decision.

#### Table 2.1. Presence

```text
Fixed = {}
Disabled = {}
Available = {PF, PS, EF, ES, RI, RF, RE}
```

| PF | PS | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| 0 | 0 | `{EF, ES, RI, RF, RE}` | `{}` | `Skip` | The file and indexed state are absent: current. |
| 0 | 1 | `{EF, RI, RF, RE}` | `{ES}` | [Table 2.2](#table-22-missing-file) | |
| 1 | 0 | `{ES, RI, RF, RE}` | `{EF}` | [Table 2.3](#table-23-new-file) | |
| 1 | 1 | `{}` | `{EF, ES, RI, RF, RE}` | [Table 2.4](#table-24-content-kind) | |

#### Table 2.2. Missing File

```text
Fixed = {PF=0, PS=1}
Disabled = {EF, RI, RF, RE}
Available = {ES}
```

| ES | Disables | Available | Decision |
| --- | --- | --- | --- |
| 0 | `{}` | `{}` | `Delete` |
| 1 | `{}` | `{}` | `Delete` |

The file is absent on disk, so its indexed state must be deleted.

#### Table 2.3. New File

```text
Fixed = {PF=1, PS=0}
Disabled = {ES, RI, RF, RE}
Available = {EF}
```

| EF | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- |
| 0 | `{}` | `{}` | `UpsertRecord` | The file is readable and absent from the index: add it. |
| 1 | `{}` | `{}` | `Skip` | The file id is unknown, so the file cannot be indexed. |

#### Table 2.4. Content Kind

```text
Fixed = {PF=1, PS=1}
Disabled = {}
Available = {EF, ES, RI, RF, RE}
```

| EF | ES | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| 0 | 0 | `{RE}` | `{RI, RF}` | [Table 2.5](#table-25-record-state-comparison) | |
| 1 | 0 | `{RI, RE}` | `{RF}` | `UpsertError` | The indexed record state differs from the file: reconcile it as error state. |
| 0 | 1 | `{RE}` | `{RI, RF}` | [Table 2.6](#table-26-record-recovery-from-error) | |
| 1 | 1 | `{RI}` | `{RF, RE}` | [Table 2.7](#table-27-error-comparison) | |

#### Table 2.5. Record State Comparison

```text
Fixed = {PF=1, PS=1, EF=0, ES=0}
Disabled = {RE}
Available = {RI, RF}
```

| RI | RF | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| same | same | `{}` | `{}` | `Skip` | The indexed record matches the file: current. |
| same | different | `{}` | `{}` | `UpsertRecord` | The indexed record differs from the file: reconcile it. |
| different | same | `{}` | `{}` | `Delete`, then `UpsertRecord` | The indexed id differs from the file: move the relation. |
| different | different | `{}` | `{}` | `Delete`, then `UpsertRecord` | The indexed id and record differ from the file: move the relation. |

#### Table 2.6. Record Recovery From Error

```text
Fixed = {PF=1, PS=1, EF=0, ES=1}
Disabled = {RE}
Available = {RI, RF}
```

| RI | RF | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| same | same | `{}` | `{}` | `UpsertRecord` | The indexed state is error but the file is readable: recover record state. |
| same | different | `{}` | `{}` | `UpsertRecord` | The indexed error state differs from the file: recover record state. |
| different | same | `{}` | `{}` | `Delete`, then `UpsertRecord` | The indexed id differs from the file: move the relation and recover record state. |
| different | different | `{}` | `{}` | `Delete`, then `UpsertRecord` | The indexed id and record differ from the file: move the relation and recover record state. |

#### Table 2.7. Error Comparison

```text
Fixed = {PF=1, PS=1, EF=1, ES=1}
Disabled = {RI}
Available = {RF, RE}
```

| RF | RE | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| same | same | `{}` | `{}` | `Skip` | The indexed error matches the file: current. |
| same | different | `{}` | `{}` | `UpsertError` | The indexed error differs from the file: reconcile it. |
| different | same | `{}` | `{}` | `UpsertError` | The indexed fingerprint differs from the file: reconcile it. |
| different | different | `{}` | `{}` | `UpsertError` | The indexed error and fingerprint differ from the file: reconcile them. |

#### Compact Post-Read Decision Tree

```text
ReadFile -> PF, file fingerprint, file id, file error
Use current GetState result -> PS, ES, indexed state
├─ PF=0, PS=0 -> Skip
├─ PF=0, PS=1 -> Delete
├─ PF=1, PS=0
│  ├─ EF=0 -> UpsertRecord
│  └─ EF=1 -> Skip
└─ PF=1, PS=1
   ├─ EF=0, ES=0
   │  ├─ RI=same, RF=same      -> Skip
   │  ├─ RI=same, RF=different -> UpsertRecord
   │  └─ RI=different          -> Delete, then UpsertRecord
   ├─ EF=1, ES=0 -> UpsertError
   ├─ EF=0, ES=1
   │  ├─ RI=same      -> UpsertRecord
   │  └─ RI=different -> Delete, then UpsertRecord
   └─ EF=1, ES=1
      ├─ RF=same, RE=same -> Skip
      └─ otherwise        -> UpsertError
```

## Chapter 3. Retry Decision

File reconciliation produces two independent decisions. The reconciliation decision from Chapter 2 determines whether and how the index should change. The retry decision determines whether the complete reconciliation operation should run again and, if so, which delay policy the scheduler should use.

A retry does not imply that the reconciliation decision failed. For example, a transient read error can be written to an already known indexed id while still requesting another reconciliation attempt because the error may disappear. Conversely, a persistent read error can be represented in the index without requesting another attempt.

Reconciliation can also discover that the held id locks do not cover the ids required by the current decision. The decision cannot be executed safely in that case, but the required ids are now known, so another attempt can acquire the correct locks.

This chapter defines only the retry decision produced by reconciliation. It does not define the scheduler implementation. A scheduler may use any queue and backoff algorithm that preserves the following contract:

| Decision | Meaning |
| --- | --- |
| `Complete` | Do not schedule another reconciliation attempt. |
| `RetryWithBackoff` | Schedule another attempt using the continuing backoff progression. |
| `RetryWithMinBackoff` | Schedule another attempt using the minimum delay and restart the backoff progression. |

The retry decision is evaluated before every normal return from full or partial reconciliation. It uses the latest file read result available on that return path and whether an id lock mismatch prevented the reconciliation decision from being executed.

As in Chapter 2, the complete input space is defined by a small set of values:

| Code | Name | Values | Meaning |
| --- | --- | --- | --- |
| `EF` | Error File | `0`, `1` | Did the latest file read produce an error? |
| `TE` | Transient Error | `0`, `1` | Is the file read error transient? |
| `IM` | Id Lock Mismatch | `0`, `1` | Do the held id locks fail to cover the ids required by the reconciliation decision? |

Sets:

```text
R = {EF, TE, IM}
```

The retry decision does not depend on which reconciliation decision was selected. It depends only on the read error and whether the selected decision can be executed under the held id locks.

### Availability Rules

| Values | Disables |
| --- | --- |
| `EF=0` | `{TE}` |

#### Table 3.1. Retry Conditions

```text
Fixed = {}
Disabled = {}
Available = {EF, TE, IM}
```

| EF | IM | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- | --- |
| 0 | 0 | `{TE}` | `{}` | `Complete` | No retry condition was observed. |
| 0 | 1 | `{TE}` | `{}` | `RetryWithMinBackoff` | The decision cannot execute under the held locks, but the required ids are now known. Retry with the minimum delay to acquire the correct locks. |
| 1 | 0 | `{}` | `{TE}` | [Table 3.2](#table-32-read-error-persistence) | |
| 1 | 1 | `{}` | `{TE}` | [Table 3.3](#table-33-id-lock-mismatch-precedence) | |

#### Table 3.2. Read Error Persistence

```text
Fixed = {EF=1, IM=0}
Disabled = {}
Available = {TE}
```

| TE | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- |
| 0 | `{}` | `{}` | `Complete` | Automatically repeating reconciliation is not expected to resolve a persistent error. |
| 1 | `{}` | `{}` | `RetryWithBackoff` | A transient error may disappear, so retry while continuing the backoff progression. |


#### Table 3.3. Id Lock Mismatch Precedence

```text
Fixed = {EF=1, IM=1}
Disabled = {}
Available = {TE}
```

| TE | Disables | Available | Decision | Reason |
| --- | --- | --- | --- | --- |
| 0 | `{}` | `{}` | `RetryWithMinBackoff` | The error could not be written to the index under the held locks. The required ids are now known, so retry with the minimum delay. |
| 1 | `{}` | `{}` | `RetryWithMinBackoff` | The id lock mismatch takes priority for this attempt. Retry with the minimum delay using the now-known ids. If the transient error persists after the lock mismatch is resolved, a later attempt will continue with regular backoff. |

#### Compact Retry Decision Tree

```text
Retry inputs -> EF, TE, IM
├─ IM=1             -> RetryWithMinBackoff
├─ EF=1, TE=1       -> RetryWithBackoff
└─ otherwise         -> Complete
```

The id lock mismatch branch comes first because it prevented the current reconciliation decision from executing and the required lock set is already known. Once that mismatch is resolved, a transient read error that remains follows the regular backoff branch on subsequent attempts.

## Chapter 4. Algorithm Implementation

This chapter combines the reconciliation decisions from Chapter 2 with the retry decisions from Chapter 3 and describes their implementation.

### Full Algorithm Scheme

Given the model from Chapter 1 and the decision tables from Chapters 2 and 3, the algorithm pseudocode looks like this:

```text
RetryDecision Reconcile(path, readCache, heldIdLocks)
    pass = Decide(path, readCache)

    switch pass.decision kind
        case Skip:
            return MakeRetryDecision(pass.lastReadResult, idLockMismatch: false)

        case Mutation:
            requiredIdLocks = GetRequiredIdLocks(pass.decision)

            if heldIdLocks is empty
                acquiredLocks = Acquire(requiredIdLocks)
                return Reconcile(path, pass.lastReadResult, acquiredLocks)
            else
                if heldIdLocks.Covers(requiredIdLocks)
                    ExecuteDecision(pass.decision)
                    return MakeRetryDecision(pass.lastReadResult, idLockMismatch: false)
                else
                    return MakeRetryDecision(pass.lastReadResult, idLockMismatch: true)

DecisionPass Decide(path, readCache)
    fingerprint = GetFingerprint(path)
    state = GetState(path)

    decision = MakePreReadDecision(fingerprint, state)

    if decision is ReadFile
        readResult = ResolveReadFile(path, readCache, fingerprint)
        decision = MakePostReadDecision(readResult, state)
        return DecisionPass(decision, lastReadResult: readResult)
    else
        return DecisionPass(decision, lastReadResult: readCache)

ReadResult ResolveReadFile(path, readCache, fingerprint)
    if readCache matches fingerprint
        return readCache
    else
        return ReadFile(path)
```

Here, `Reconcile` represents the general scheme for reconciling a file into the index. It calls the reconciliation decision function, acquires id locks depending on the result, executes a confirmed mutation, and evaluates the retry decision before every normal return.

`Decide` implements one complete reconciliation decision pass. It collects the data for analysis, calls `MakePreReadDecision`, reads the file when required, and then calls `MakePostReadDecision`. It carries the latest read result together with the reconciliation decision so the same observation can also participate in the retry decision. When the current pass does not read the file, the cached result from the previous pass remains the latest read result.

`ResolveReadFile` reads the file or returns the cached state.

`MakeRetryDecision` maps the available read error and id lock state through the tables from Chapter 3.

### Implementation Architecture

The implementation should map the rulebook concepts to a small set of classes. The class names below describe roles. They are not final production names.

```text
FileReconciler
├─ DecisionMaker
├─ DecisionExecutor
└─ RetryDecisionMaker
```

`FileReconciler` implements the file-to-index reconciliation process described in Chapter 4. It owns the scenario flow: collecting observations, coordinating locks, calling the decision maker, and asking the executor to apply confirmed terminal decisions.

`FileReconciler` should expose two reconciliation entry points:

| Entry point | Used by | Meaning |
| --- | --- | --- |
| Full reconciliation | File system event processing | Runs the complete algorithm. |
| Partial reconciliation | User command processing after reading a file | Continues reconciliation from already available data. The caller has already read the file and already holds the relevant lock. |

`DecisionMaker` owns the pure decision tables from Chapter 2. It has no file system access, no index mutation access, no locks, and no retry logic. Its responsibility is limited to mapping already collected observations to a decision:

```text
MakePreReadDecision(fingerprintObservation, indexedStateObservation)
MakePostReadDecision(readObservation, indexedStateObservation)
```

`DecisionExecutor` applies a confirmed terminal decision to locked index scopes. It does not choose when to read a file, when to acquire locks, or when to retry. It only performs the side effects of terminal decisions:

| Decision | Executor action |
| --- | --- |
| `Delete` | Remove the path from the indexed id. |
| `UpsertRecord` | Add or refresh readable record state. |
| `UpsertError` | Add or refresh error state for the indexed id. |
| `Delete`, then `UpsertRecord` | Move the path from the indexed id to the id read from the file. |

`RetryDecisionMaker` owns the pure retry tables from Chapter 3. It has no file system access, no index mutation access, and no scheduling logic. Before each normal return, `FileReconciler` gives it the latest available read error and the id lock mismatch state. The resulting retry decision is returned to the scheduler.
