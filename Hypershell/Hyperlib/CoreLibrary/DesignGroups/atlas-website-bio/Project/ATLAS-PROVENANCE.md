# Atlas Universalis portable Design Groups

Source design: Atlas.Web at a8c9608cd54d58e525961ce0ad0ca4ecad58c24a,
Pages/Shared/_Layout.cshtml, Index, Services, Bio, ProjectLab, LaunchScope,
wwwroot/css/atlas.tokens.css and site.css. Admin patterns derive from
Hypershell.ProfileServer/Pages and wwwroot/css/admin.css. Archives patterns
derive from HyperLib.Archives/wwwroot/index.html, archives.css and archives.js.

The bundled 96px and 512px artwork is the approved current Atlas emblem.
The portable source uses semantic Razor components, isolated local CSS,
browser-native controls and a local ES module. Atlas hosting, MVC PageModel,
tag helpers, authentication, credential stores and publication APIs are not
dependencies. Form samples only validate or preview in-memory page state.
LaunchScope retains the original deterministic scope/risk rules. Archives
reading and filtering operate on explicitly supplied local content.

Typography uses the destination Work's font selection and system fallbacks;
it never requests Google Fonts. Individual objects inherit Work colors;
import the Atlas palette or Work style to reproduce the navy/gold design.
Responsive navigation remains accessible on small screens. Decorative
animation respects reduced-motion preference. Root-relative Atlas asset
references have been replaced by local RCL assets so cross-template imports
carry their media and JavaScript with them.

This group contains:
- HLC-ATLAS-NAVIGATION: Atlas.Web/Pages/Shared/_Layout.cshtml:24; site.css:93
- HLC-ATLAS-SECTION-HEADING: Atlas.Web/Pages/Services.cshtml:8; Bio.cshtml:9; ProjectLab.cshtml:10
- HLC-ATLAS-PORTRAIT: Atlas.Web/Pages/Bio.cshtml:16-33; site.css:609
- HLC-ATLAS-TIMELINE: Atlas.Web/Pages/Bio.cshtml:35-65; site.css:688
- HLC-ATLAS-TIMELINE-ENTRY: Atlas.Web/Pages/Bio.cshtml:38; site.css:696
- HLC-ATLAS-SOCIAL-LINKS: Atlas.Web/Pages/Bio.cshtml:67-104; site.css:724
- HLC-ATLAS-PROJECT-CARD: Atlas.Web/Pages/ProjectLab.cshtml:21-37; site.css:792
- HLC-ATLAS-PROJECT-GRID: Atlas.Web/Pages/ProjectLab.cshtml:20-116; site.css:785
- HLC-ATLAS-COMMAND-BLOCK: Atlas.Web/Pages/ProjectLab.cshtml:34-36; site.css:858
- HLC-ATLAS-FOOTER: HyperLib.Archives/wwwroot/index.html:65; archives.css:264