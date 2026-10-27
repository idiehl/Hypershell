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
- HLC-ATLAS-HERO: Atlas.Web/Pages/Index.cshtml:7; site.css:226
- HLC-ATLAS-STARFIELD: Atlas.Web/wwwroot/css/site.css:292-336
- HLC-ATLAS-MAP-GRID: Atlas.Web/wwwroot/css/site.css:243-290; Admin/admin.css:84
- HLC-ATLAS-EMBLEM: Atlas.Web/wwwroot/brand/icon-512.png; _Layout.cshtml:31
- HLC-ATLAS-SECTION-HEADING: Atlas.Web/Pages/Services.cshtml:8; Bio.cshtml:9; ProjectLab.cshtml:10
- HLC-ATLAS-PRINCIPLES: Atlas.Web/Pages/Index.cshtml:39-59; site.css:497
- HLC-ATLAS-PRINCIPLE-CARD: Atlas.Web/Pages/Index.cshtml:47; site.css:550
- HLC-ATLAS-SERVICES: Atlas.Web/Pages/Services.cshtml:17-59; site.css:933
- HLC-ATLAS-SERVICE-CARD: Atlas.Web/Pages/Services.cshtml:18; site.css:939
- HLC-ATLAS-ACTION-BANNER: Atlas.Web/Pages/Services.cshtml:62-69; site.css:961
- HLC-ATLAS-BUTTON-BAR: Atlas.Web/wwwroot/css/site.css:172-212; LaunchScope.cshtml:81
- HLC-ATLAS-FOOTER: HyperLib.Archives/wwwroot/index.html:65; archives.css:264