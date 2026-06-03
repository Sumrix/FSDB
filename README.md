# FSDB

FSDB is a local-first file-system database for .NET.

It is built around a simple idea: the file system itself should be a user-friendly API for the database. A table is a folder, a record is a file, and the data remains readable, editable, movable, and inspectable with ordinary operating system tools.

## Status

FSDB is in active development. The project works, but it is still being polished for stability, API clarity, and production readiness.

It is not recommended for real-world use yet. At this stage, the repository is mainly useful for following the design, experimenting locally, and discussing the model.

## What It Does

FSDB:

- treats files as database records;
- exposes a table-like API with primary keys;
- lets users create, edit, rename, move, and delete records directly through the file system;
- keeps an internal file index for fast access to cached fields;
- safely handles file-system errors and delayed file-system watcher events;
- supports schema-versioned records and migrations;
- is being built with trimming and NativeAOT support in mind.

## Who Is It For?

FSDB is for applications where users should have direct access to their data files.

It can fit scenarios such as chat history, game saves, settings presets, editable local content, or other data that users may want to inspect, edit, back up, sync, share, or put under source control without a special database tool.

## Quick Start

```csharp
using FSDB;
using FSDB.Tables;
using FSDB.Tables.Building;

var users = TableDefinitionBuilder.CreateDefault<string, User>();

await using var db = await FileSystemDatabase.StartAsync(
    rootPath: "data",
    tableDefinitions: [users]);

var table = db.Table<string, User>();

await table.UpsertAsync(new User(
    Id: "alice",
    DisplayName: "Alice",
    Email: "alice@example.com"));

User? alice = await table.GetAsync("alice");

await table.DeleteAsync("alice");

public sealed record User(
    string Id,
    string DisplayName,
    string Email) : IRecord<string>;
```

This creates a database directory, creates a table directory for `User`, writes a user record as a file, reads it back through the table API, and then deletes it.
