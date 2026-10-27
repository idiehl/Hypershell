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
- HLC-ATLAS-SCOPE-PLANNER: Atlas.Web/Pages/LaunchScope.cshtml; LaunchScope.cshtml.cs:33-155
- HLC-ATLAS-RISK-BADGE: Atlas.Web/Pages/LaunchScope.cshtml:113; site.css:1141
- HLC-ATLAS-RESULT-SUMMARY: Atlas.Web/Pages/LaunchScope.cshtml:118-174
- HLC-ATLAS-FORM-FIELD: Atlas.Web/Pages/LaunchScope.cshtml:22-77; Admin/admin.css:395
- HLC-ATLAS-CONTACT-FORM: Atlas.Web/Pages/Account/Create.cshtml:17; LaunchScope form styling
- HLC-ATLAS-FOOTER: HyperLib.Archives/wwwroot/index.html:65; archives.css:264