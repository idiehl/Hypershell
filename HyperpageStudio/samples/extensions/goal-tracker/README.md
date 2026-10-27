# Project Goal Tracker sample

This approved local WebView package demonstrates Hypershell panel host API v1.
It requests `WorkspacePanel` and `WorkspaceMetadata`, persists its selected
stage through extension-local device state, and updates its project/tab labels
from bounded read-only host context.

Copy this complete folder beneath:

`%LOCALAPPDATA%\Hypershell\extensions`

Then open **Settings > Extensions > Refresh discovery**, review the manifest,
capabilities, package hash, and `PANEL_MESSAGE_API_DECLARED` diagnostic, and
choose **Approve local panels**. The panel can replace either sidebar or open
at the Bottom or Floating host.

The sample cannot read files or document contents, inspect terminal output,
execute commands, access the network, retrieve secrets, load native code, or
invoke AI tools. Its client implementation is `panel/panel.js`; the complete
protocol and security boundary are documented in
`doc/EXTENSION_PANEL_MESSAGING.md`.
