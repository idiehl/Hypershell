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
- HLC-ATLAS-ADMIN-HEADER: Hypershell.ProfileServer/Pages/Shared/_Layout.cshtml:20
- HLC-ATLAS-STATUS-STRIP: Hypershell.ProfileServer/Pages/Index.cshtml:13; Storage.cshtml:13; admin.css:257
- HLC-ATLAS-METRIC: Hypershell.ProfileServer/wwwroot/css/admin.css:277
- HLC-ATLAS-MODULE-CARD: Hypershell.ProfileServer/Pages/Index.cshtml:19; admin.css:320
- HLC-ATLAS-INVENTORY: Hypershell.ProfileServer/Pages/Profiles.cshtml:20-62; admin.css:675
- HLC-ATLAS-INVENTORY-ROW: Hypershell.ProfileServer/Pages/ClientDevices.cshtml:27; admin.css:680
- HLC-ATLAS-NOTICE: Hypershell.ProfileServer/wwwroot/css/admin.css:541
- HLC-ATLAS-EMPTY-STATE: Hypershell.ProfileServer/Pages/Downloads.cshtml:15; admin.css:769
- HLC-ATLAS-LOGIN-FORM: Hypershell.ProfileServer/Pages/Account/Login.cshtml; admin.css:377
- HLC-ATLAS-CODE-ENTRY: Hypershell.ProfileServer/Pages/Device.cshtml:24; admin.css:559
- HLC-ATLAS-APPROVAL: Hypershell.ProfileServer/Pages/CredentialRelease.cshtml:45-70; admin.css:571
- HLC-ATLAS-INLINE-EDITOR: Hypershell.ProfileServer/Pages/AdminSection.cshtml:102-116; admin.css:744
- HLC-ATLAS-DOWNLOAD-CARD: Hypershell.ProfileServer/Pages/Downloads.cshtml:23-40
- HLC-ATLAS-FOOTER: HyperLib.Archives/wwwroot/index.html:65; archives.css:264