# Meridian Library organization

The Library is an ordinary editable Work. Its subject collections are ordinary
Container nodes with Work portals as descendants. There is no separate collection
record, collection database, or new node type.

## Initial subject containers

| Subject | Contents | Container GUID |
| --- | --- | --- |
| Coding | C++ and C# atlases, with their existing subordinate Works | `7d5bf0a1-bf76-4f8b-b743-7ec8667978f4` |
| Mythology | The three Greek mythology collections and their existing Works | `a3d00829-1577-4dec-8591-24495b6c885d` |
| Tutorial | Meridian Tutorial: Learn by doing, followed by its eleven parts | `f67d1f2e-9f63-48c6-961e-b512505d2d98` |

This scheme follows the user's October 21, 2026 instruction. The Library's root
label is decorative; it is not a fourth subject category. Containers can be
renamed, expanded, collapsed, and arranged with the normal tools. A new subject
may receive another ordinary container when the content justifies it.

## Placing a saved Work

1. Inspect the live saved Library, catalog, and recovery state. A project being
   present in the catalog does not prove that its launcher is visible or suitably
   placed on the Library Work.
2. Reuse an existing launcher and its original destination when it already exists.
   Put its hierarchy beneath the appropriate subject. Do not duplicate a Work
   merely to place it in a collection.
3. Keep the initial view concise: show collection entry portals, with their
   subordinate Work launchers collapsed until needed. Keep useful Tags and a clear
   title on each collection cover for the collapsed state.
4. Preserve unrelated Library Works, node identities, snapshots, embedded assets,
   meaningful links, and discovery records. Discovery keys without a corresponding
   portal can represent intentional user deletions. Do not resurrect them during
   reorganization or use catalog reconciliation as an excuse to undo deletions.
5. Review the actual editor at overview and reading scale. Click each entry portal
   and verify its project and Work destination. Verify collapse, save, reopen,
   and reexpand with the same original identities.

## Saving an organizational change

Prepare a declarative local candidate and preserve a baseline before modifying the
saved Library. Use the current expected revision and retain a named checkpoint
of the original. The Library save endpoint currently has revision checks but no
atomic no-recovery guard: review recovery and compare the saved baseline again
immediately before writing. Respect conflicts and retain user drafts. A Library
content change does not require restarting Meridian or replacing its application.

The October 21 composition and verification helpers are documented in
[scripts/library-tutorial/README.md](../scripts/library-tutorial/README.md).
Tutorial sources and maintenance are described in
[MERIDIAN-TUTORIAL.md](MERIDIAN-TUTORIAL.md).
