# Hyperlib workspace concepts

Hyperpage Studio and Hyperlib Editor use a dedicated outer tab strip for Template, Work, and Page objects. The return arrow restores the previous Hypershell workspace without closing either set of tabs.

## Template

A Template is a library of reusable elements: text boxes, media boxes, widgets, and tools. Its workspace opens on Elements and their editable fields. Existing layout, region, and style definitions remain available under Advanced for compatibility.

Reusable Templates are starting points. Creating a Work copies the chosen Template into the Work's independent project. Editing a reusable Template affects future Works; existing Works keep their own definitions.

## Work

A Work has two responsibilities: it contains Pages and organizational containers such as Volumes, Parts, and Chapters; it defines page structure and shared appearance using elements from its Template. Its workspace has Contents, Structure, and Appearance sections.

Contents shows the actual nested hierarchy and offers container creation through the revision-checked library gateway. Structure edits starting layout, insertion regions, and Work-specific elements. Appearance edits shared background, panel, accent, and text colors, fonts, and text size. Advanced source and schema controls remain available when required.

Appearance changes use the library's existing revision-checked operations and Undo/Redo. Work-specific values preserve other custom appearance settings. Unsaved appearance drafts stay associated with their Work while switching, refreshing, or rebuilding the view; closing requires saving or explicitly discarding them. These appearance drafts are in-memory and are not crash-recovery records.

## Page

A Page is primarily created and written in Hyperlib. The Editor's Write in Hyperlib actions open the selected Work's writing workspace; opening an existing Page opens that Page directly. Page creation is no longer a native Editor dialog in this workflow.

A Page arranges available elements and supplies its own text, media, and values. It inherits its Work's structure and appearance, with existing Page-specific overrides retained. Hyperlib opens as a Page object tab without browser address/navigation chrome. Page tabs flush pending content edits before closing and stay open if saving cannot be confirmed.

Source and preview remain available through secondary actions. The focused Hyperlib workspace suppresses generic pane creation, docking, side-panel controls, and the extra toolbar. Ordinary Hyperpage projects retain their authoring tools while using the dedicated outer object strip.
