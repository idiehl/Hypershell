# Contextual Command Guide and AI foundation

## Implemented baseline

The Command Builder remains the single place where a command is assembled.
After an Action and Object resolve to a command, an inline Command Guide appears
in the Builder. It describes that exact selection and can show:

- a concise summary and a fuller explanation;
- the command's syntax;
- practical code examples with explanations;
- optional supporting images supplied by command-library metadata;
- a visible source label for the documentation.

PowerShell guides load local `Get-Help` content and examples when they are
available. If local prose is unavailable, the guide falls back to a clearly
labeled metadata-derived explanation. C# guides are data-driven and live beside
their command definitions in the JSON command library.

There is intentionally no separate searchable Dictionary page. The guide is
contextual help for the current Action-Object choice, not a second command
selection workflow.

## AI seam

`ICommandDefinitionAgent` is provider-neutral. It receives:

- term, language, library, category, and command type;
- syntax and the current definition;
- the active Builder Action, Object, and command profile;
- typed parameter metadata.

The default implementation is intentionally unconfigured and performs no
network request. It remains separate from the implemented AI workspace
conversation in `AI_WORKSPACE_PROVIDER.md`: that workspace feature can inspect
approved files and prepare reviewed mutations, but it does not generate or
persist Command Guide definitions. Guide generation still needs its own
provenance, review, versioning, and rollback workflow before this seam is
activated.

## Library metadata

Each command may provide a `definition` and an `examples` array. An example
supports `title`, `code`, `explanation`, `imagePath`, and `imageAltText`.
Relative image paths are resolved from the directory containing the library
JSON file. Image fields are optional; the UI collapses them when absent.

## Decisions for the next phase

- cloud provider, local model, or user-selectable providers;
- model and credential storage;
- whether source code, paths, command history, or terminal output may be sent;
- generated-guide review, approval, persistence, versioning, and rollback;
- citation and provenance requirements;
- caching, rate limits, offline behavior, and cost controls;
- broader agent capabilities such as command explanation, flag suggestions,
  pipeline review, and terminal-error diagnosis.
