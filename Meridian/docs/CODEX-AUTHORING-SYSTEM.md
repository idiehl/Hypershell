# A durable Codex authoring system for Meridian

The current product priority is authoring from **ordinary ChatGPT desktop**,
without requiring users to run Codex or have a source checkout. The local Codex
workflow below is the existing foundation, not the completed ChatGPT connection.
Read [CHATGPT-AUTHORING-OPTIONS.md](CHATGPT-AUTHORING-OPTIONS.md) for the current
implementation audit, supported delivery options, distribution constraints, and
the recommended skill-plus-MCP plugin. A reusable hierarchy-layout and routing
tool is also still needed; simple row/column/grid/radial placement is not a
complete graph composition engine.

The recommended starting point is a reusable **skill plus maintained project
standards and approved examples**. The existing declarative authoring API is the
execution layer. A plugin can distribute the same skill and a future connector;
a separately trained agent is not required to begin.

## Layers

| Layer | Current role |
| --- | --- |
| `AGENTS.md` | Short persistent routing and project invariants. Directs authoring sessions to the skill and maintained standards. |
| `docs/WORK-AUTHORING-STANDARDS.md` | One maintained source for accepted axioms, inherited rules, proposals and review records. |
| `.agents/skills/meridian-authoring/SKILL.md` | Reusable inspect → compose → render → revise workflow, using the real API and approved principles. |
| Recipes, alias maps and reviewed examples | Reproducible compositions with original identities, original source material, and overview/detail evidence. |
| API validation and focused checks | Enforce mechanical guarantees and verify interactions; support visual review without claiming to automate taste. |

The personal skill discovery entry points at the project-owned source so that
future local Codex sessions can find the workflow even when the current project
is Hypershell. The canonical skill and standards stay beside Meridian. This is
local persistence; it is not automatic synchronization to every machine or
every user's account. For broader installation and distribution, package the
same maintained workflow as a Codex plugin when that is needed.

## Establish the baseline through real Works

Review a selected Work with the user, identify what is successful and what
should change, and turn only the accepted decisions into scoped principles.
Keep each principle's rationale, examples, observable criterion and exceptions.
Review contrasting kinds of composition so a genealogy's useful rule does not
become an arbitrary rule for a reading room or illustrated essay.

Useful first candidates from the existing collection are the Visual atlas's
selected divine family and narrative comparison; a Gods dossier and four-book
reading Window; and a Living atlas branch with nested disclosure. These are
proposed review candidates, not approved visual standards. The user decides
which properties should become axioms.

When enough examples are approved, use them as regression cases: compose a
small new Work using the skill, inspect it at overview/detail scale, compare the
observable results with the approved properties, and revise the instructions
only where the evidence shows a gap. Do not build a large universal rulebook
from isolated mistakes.

## Commands and future connectors

The existing `scripts/author.mjs` supports inventory, composition, inspection,
focused reader capture, publication and guarded local application. Its current
documentation is `docs/AUTHORING.md`. The user-facing capture filter is `--work`;
older machine names remain compatible.

A future MCP connector can expose these typed operations to ordinary ChatGPT and Codex when
that improves discovery or avoids shell orchestration. It should wrap the same
validation, identities, revision/recovery guards and review evidence. A connector
provides capability; the skill and approved standards provide judgment.

Plugins are the later distribution package for skills and optional connectors.
Subagents can perform independent source, visual or behavioral review, but their
temporary role prompts should not be the sole location of lasting rules.

## Official documentation consulted

- [Codex customization](https://learn.chatgpt.com/docs/customization/overview)
- [Build skills](https://learn.chatgpt.com/docs/build-skills)

Official guidance distinguishes persistent project instructions, reusable skills,
connectors, subagents and plugin distribution. The local files in this project
implement the initial authoring foundation; no online publishing or account
system is part of this setup.
