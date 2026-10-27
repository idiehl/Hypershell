# Project Work Items

Hypershell's Work Items side tool is a lightweight project backlog for small
problems, improvements, and mini-projects that should not interrupt the current
development cycle.

## Storage and scope

Each list is stored at:

```text
<project-root>/.hypershell/work-items.json
```

The file is UTF-8 JSON and can be versioned, synchronized, or inspected by
other development agents. Saved Explorer projects use their first available
root directory. When no saved project covers the current Explorer folder, the
folder is offered as a fallback project scope.

Attachments are copied without modifying the source file and stored beneath:

```text
<project-root>/.hypershell/work-items/attachments/<item-id>/
```

The JSON document stores relative attachment paths so the tracker remains
portable with its project.

## User workflow

The left workspace dock includes a `Project Work Items` tool. A user can:

- choose a project or folder backlog;
- add a short reference title, full description, and zero or more attachments;
- star an item for consideration during the next one to three work cycles;
- expand, edit, or delete an item;
- drag rows to set priority, with the most important item at the top; and
- reload the JSON file after an external tool changes it.

Changes are saved atomically. A malformed or unsupported JSON document is not
silently overwritten.

## AI behavior

When a Hypershell AI request begins, the selected project's current ordered
list is added as secondary context if—and only if—the active AI Context grants
read access to the tracker path. The assistant is instructed to remain focused
on the user's current request and incorporate only compatible items. Starred
items receive preference, but a star does not start an autonomous background
job.

The repository-level `AGENTS.md` gives Codex the same backlog convention when
it works directly in the Hypershell repository.

## Schema version 1

The root document contains `schemaVersion`, `projectId`, `projectName`,
`updatedAtUtc`, and an ordered `items` array. Each item contains a stable ID,
title, description, starred state, order, timestamps, and attachment metadata.
