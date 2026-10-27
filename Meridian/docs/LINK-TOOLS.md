# Links, pivots, and joints

Choose **Link** from the `+` menu to draw connections directly on the canvas. The tool stays active after finishing a connection.

1. Click a node to begin the link.
2. Click empty canvas space to add a pivot wherever the path should turn. You can add several pivots.
3. Click the destination node to finish the connection.

The unfinished path is a preview. Its links and pivots become part of the canvas together when you finish, as one undoable change. Press **Escape** to cancel the preview. Holding or dragging the background continues to pan without adding a pivot.

Choose **Hand** to inspect or move objects. Click a pivot or joint to select it; the side panel provides the same Properties and Appearance menus as other nodes. Drag a utility node to reshape every connected path. Utility nodes have their own GUIDs.

## Utility nodes

The node creation form and Properties panel use **Utility** as the type, with two sub-types:

- **Pivot** changes the direction of a path.
- **Joint** is a shared meeting point. Connect several incoming links to the same joint, then connect the joint to the destination once to share the outgoing path.

Utilities can appear as a **Dot** or a **Line**, with editable dimensions, shape, fill, border, and color. They begin as small fixed-size objects so they do not introduce a card or title into the canvas.

With **Node** selected, click a link's line to insert a utility at that point. The form lets you choose Pivot or Joint before committing. The original link's relationship, direction, appearance, and route are retained around the insertion. Right-click a link to create a Pivot or Joint directly from the menu.

While drawing a link, clicking an existing line finishes at a new Joint on that line. It splits the existing route and joins the new route in one change. To reuse an existing joint, click that joint instead of the line.

## Link appearance

Select a link and choose **Appearance** in the side panel to edit:

- **Line color**
- **Line width**, from 1 to 32 canvas pixels
- **Line style**: Solid, Dashed, or Dotted

These controls apply to visual, Boolean, and numeric links. Relationship and direction remain separate from appearance. The right-click **Create Link** form also offers these controls along with endpoint, ownership, and computed-input options.

Changes save automatically and are retained in the offline viewer. Connections between a container and its embedded children remain available in the Links panel without a redundant line on the canvas.
