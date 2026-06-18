## What is this?

This folder is a small adapter layer over `Microsoft.Extensions.Logging` that automatically adds the table name to logger scopes. It exists to keep table-aware logging consistent without repeating manual `BeginScope` plumbing throughout the codebase.
