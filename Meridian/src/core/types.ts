import type { FontFamily } from './fonts.ts';
export type { FontFamily } from './fonts.ts';
export type NodeKind = 'canvas' | 'container' | 'spreadsheet' | 'module' | 'media' | 'block' | 'text' | 'image' | 'document' | 'input' | 'number' | 'switch' | 'output' | 'joint' | 'junction' | 'reference' | 'canvas-link';
export type NodeContentType = 'text' | 'asset' | 'link';
export type Gate = 'AND' | 'OR' | 'NOT' | 'NAND' | 'NOR' | 'XOR' | 'XNOR';
export type Operation = Gate | 'IDENTITY' | 'ADD' | 'SUBTRACT' | 'MULTIPLY' | 'DIVIDE' | 'MODULO' | 'POWER' | 'MIN' | 'MAX' | 'ABS' | 'NEGATE' | 'EQUAL' | 'GREATER' | 'LESS';
export type ValueType = 'boolean' | 'number';
export type LinkKind = 'visual' | 'functional' | 'numeric';
export type LinkRelation = 'causal' | 'chronological' | 'mutual' | 'cyclical' | 'generic';
export type LinkDirection = 'forward' | 'reverse' | 'both' | 'none';
export type UtilityVariant = 'pivot' | 'joint';
export type ChildLayout = 'contained' | 'spatial';
export type ChildPlacement = 'free' | 'near' | 'edge';
export type AttachSide = 'auto' | 'top' | 'right' | 'bottom' | 'left';
export type ChildArrangement = 'free' | 'grid' | 'row' | 'column' | 'radial' | 'stack';
/** Spatial is accepted only to read older documents; contained is the persisted Freeform pattern. */
export type ContainerVariant = 'spatial' | 'contained' | 'horizontal' | 'vertical' | 'grid' | 'spreadsheet' | 'gallery' | 'window';
export interface GridSettings {
  columns: number; rows: number; cellWidth: number; cellHeight: number; gap?: number; showHeaders?: boolean;
  /** Traversal order for equal-size ordered child tiles. */
  fillOrder?: 'row' | 'column';
  /** Window viewport dimensions; the complete address space remains columns × rows. */
  windowColumns?: number; windowRows?: number; scrollDirection?: 'vertical' | 'horizontal';
}
export interface CellLocation { column: number; row: number; name?: string }
export type LogicValue = boolean | number | null;
export interface Point { x: number; y: number }
/** Presentation-only origin of an exterior disclosure tray, relative to its original source node. */
export interface ExpansionTray {
  offset: Point;
  /** Version 2 removed the banner; version 3 uses the compact 5px container inset. */
  layoutVersion?: 2 | 3;
  /** Optional authored outer frame. Omission keeps the legacy content-sized tray. */
  size?: { width: number; height: number };
}
export interface Rules { nodeKinds?: NodeKind[]; linkKinds?: LinkKind[]; operations?: Operation[] }
export interface SnappingSettings { stickyEdges?: boolean; snapToGrid?: boolean; smartAlign?: boolean }
export type NodeShape = 'rectangle' | 'square' | 'rounded' | 'circle' | 'ellipse' | 'triangle' | 'diamond' | 'pentagon' | 'hexagon' | 'octagon' | 'none';
export type BackgroundPattern = 'none' | 'dots' | 'grid' | 'isometric' | 'linen' | 'contours' | 'aurora' | 'stars';
export type PortalStyle = 'doorway' | 'passage' | 'wormhole' | 'vortex';
export interface BackgroundStyle {
  backgroundPattern?: BackgroundPattern;
  /** World-space tile spacing; cover/contain images use the available surface. */
  backgroundScale?: number;
  backgroundTint?: string;
  /** Local embedded raster image, independent of a node's displayed media. */
  backgroundAssetId?: string;
  backgroundFit?: 'cover' | 'contain' | 'tile';
  backgroundOpacity?: number;
}
export interface NodeStyle extends BackgroundStyle {
  /** Keep text and content visible while revealing the backdrop only on hover. Omitted means always. */
  backgroundDisplay?: 'always' | 'hover';
  /** Optional launcher override; omitted follows the containing canvas, then Endless doorway. */
  portalStyle?: PortalStyle;
  /** Optional six-digit hex tint; omitted follows the workspace, then the design's original palette. */
  portalColor?: string;
  fill?: string; border?: string; textColor?: string; accent?: string;
  fontFamily?: FontFamily; fontSize?: number; titleSize?: number;
  /** New-node typography: centered composition and automatic title-driven width. Omission preserves authored legacy sizing. */
  textLayout?: 'balanced';
  /** Local text-block angles in degrees. The backdrop and media remain unrotated; omitted means zero. */
  textRotation?: number; titleRotation?: number;
  /** Fit the content composition to its frame by default; fixed retains authored typography. */
  contentSizing?: 'fit' | 'fixed';
  textAlign?: 'left' | 'center' | 'right'; variant?: 'card' | 'label'; imageFit?: 'contain' | 'cover';
  borderWidth?: number; borderStyle?: 'solid' | 'dashed' | 'dotted' | 'double'; radius?: number; shape?: NodeShape;
  /** Spacing in canvas pixels: content and child-border inset default to 5. */
  padding?: number; contentGap?: number; textMediaGap?: number; childPadding?: number;
}
export interface CanvasStyle extends BackgroundStyle {
  background?: string; grid?: string; gridVisible?: boolean;
  /** Default launcher appearance for this workspace; omitted means Endless doorway. */
  portalStyle?: PortalStyle;
  /** Optional six-digit hex tint shared by canvas launchers without their own override. */
  portalColor?: string;
}
export interface LinkStyle { color?: string; width?: number; lineStyle?: 'solid' | 'dashed' | 'dotted'; /** Legacy fallback when lineStyle is omitted. */ dashed?: boolean }
/** A conceptual GUID relationship. It never renders a line or starts a network request. */
export interface RemoteLink { id: string; targetId: string; relation: LinkRelation; label?: string }
export interface NodeHyperlink { url: string; label?: string }
/** Inert overrides applied to a passage; absent fields inherit the node's typography. */
export interface TextFormat {
  fontFamily?: FontFamily; fontSize?: number; color?: string;
  bold?: boolean; italic?: boolean; underline?: boolean;
}
/** Half-open UTF-16 offsets into the canonical title or text, never inside a surrogate pair. */
export interface TextRun { start: number; end: number; style: TextFormat }
/** Ordered inert content owned by one node. Only the first item is shown on its tile. */
export type NodeContentItem =
  | { id: string; type: 'text'; text: string; textRuns?: TextRun[] }
  | { id: string; type: 'asset'; assetId: string }
  | { id: string; type: 'link'; url: string; label?: string };
/** Independently authored frame for a bounded container's disclosure mode. */
export interface ContainerSize { width: number; height: number; sizeMode: 'auto' | 'fixed' }
export interface ContainerSizes { expanded?: ContainerSize; collapsed?: ContainerSize }
/** A named disclosure group uses existing containment while presenting a compact launcher when closed. */
export interface DisclosureCluster { buttonShape: 'square' | 'circle' }
export type SmartLinkDirection = 'auto' | 'right' | 'down' | 'left' | 'up';
/** An opt-in routing rail. Its immediate children are its ordered layout members. */
export interface SmartLinkSettings {
  direction: SmartLinkDirection;
  /** Clear spacing between the currently visible branch footprints, in world pixels. */
  gap: number;
  /** Stable automatic orientation; changing direction clears this hint. */
  resolvedDirection?: Exclude<SmartLinkDirection, 'auto'>;
}
/** Inert recoverable content from retired legacy Block nodes; never displayed as active Media content. */
export interface RetiredBlockContent { title: string; text?: string; titleRuns?: TextRun[]; textRuns?: TextRun[]; contentType?: NodeContentType }
export interface MeridianNode {
  id: string; canvasId: string; parentId: string | null; kind: NodeKind; title: string;
  /** Stable sidebar/search name, independent of the optional visible content title. */
  tag?: string;
  x: number; y: number; width: number; height: number;
  /** Anchor this node's canvas coordinates until explicitly unlocked. */
  positionLocked?: boolean;
  /** Automatic tiles fit visible content; fixed tiles retain deliberately authored dimensions. */
  sizeMode?: 'auto' | 'fixed';
  /** The active frame stays in width/height; mode switches restore the other saved frame. */
  containerSizes?: ContainerSizes;
  /** Independent from edge locks: any sibling branches can disclose together without touching. */
  cluster?: DisclosureCluster;
  /** Utility-owned dynamic layout; membership uses ordinary parent/child identities. */
  smartLink?: SmartLinkSettings;
  retiredBlockContent?: RetiredBlockContent;
  collapsed?: boolean; text?: string; assetId?: string; value?: boolean | number; valueType?: ValueType; closed?: boolean;
  titleRuns?: TextRun[]; textRuns?: TextRun[];
  /** Exactly one primary content item is displayed; inactive legacy fields remain recoverable. */
  contentType?: NodeContentType;
  /** Omitted reads legacy fields without migrating. Empty explicitly clears the display slot. */
  contentItems?: NodeContentItem[];
  /** The canonical title remains available to search and the Hub while hidden on the tile. */
  showTitle?: boolean;
  /** New automatic children use the parent's display pattern; manual and legacy positions remain stable. */
  childPosition?: 'auto' | 'manual';
  /** Exterior ordered surfaces keep a collision-free origin relative to the parent tile. */
  childOffset?: Point;
  /** Original descendants keep their identities and parents; only their tray's remembered origin is stored here. */
  expansionTray?: ExpansionTray;
  operation?: Operation; targetId?: string; exposed?: boolean; rules?: Rules; snapping?: SnappingSettings; style?: NodeStyle;
  /** A launcher destination outside this document. Forking preserves the original destination. */
  canvasTarget?: { projectId: string; canvasId?: string };
  /** Gallery presents one immediate child inside the parent; other variants use their layout fields. */
  containerVariant?: ContainerVariant;
  /** Compact routing utility; older junction nodes are interpreted as joints. */
  utilityVariant?: UtilityVariant;
  dependencyIds?: string[];
  remoteLinks?: RemoteLink[];
  /** Additional inert attachment assets; assetId remains the primary displayed media. */
  attachments?: string[];
  hyperlinks?: NodeHyperlink[];
  /** Spatial parents disclose children on the same canvas without becoming an enclosing frame. */
  childLayout?: ChildLayout;
  /** Placement used when the parent reveals this node. Omission preserves authored coordinates. */
  placement?: ChildPlacement;
  attachSide?: AttachSide;
  /** Addressable grid surface, also available on ordinary formal containers. */
  grid?: GridSettings;
  /** One-based location in the immediate parent's grid; identity remains this node's GUID. */
  cell?: CellLocation;
  /** Address or named cell inside targetId. Cross-canvas reads use only saved snapshots. */
  targetCell?: string;
  childArrangement?: ChildArrangement;
}
export interface MeridianLink {
  id: string; canvasId: string; sourceId: string; targetId: string;
  /** Node whose expansion reveals this connection. Omission infers the nearest common ancestor, including endpoints. */
  parentId?: string;
  kind: LinkKind; relation: LinkRelation; direction?: LinkDirection;
  targetPort?: number; sourceAnchor?: Point; targetAnchor?: Point; points?: Point[];
  label?: string; style?: LinkStyle;
  /** An edge contact has no shaft. Locked contacts form rigid, reusable clusters. */
  snap?: { locked: boolean };
}
export interface MeridianCanvas { id: string; rootId: string; title: string; snapping?: SnappingSettings; style?: CanvasStyle }
export interface MeridianAsset { id: string; name: string; mime: string; base64: string }
export interface SavedSnapshot { node: MeridianNode; value: LogicValue; asset?: MeridianAsset; backgroundAsset?: MeridianAsset; assets?: MeridianAsset[]; savedAt: string }
export interface ExternalDependency { canvasId: string; title: string; required: boolean }
export interface MeridianProject {
  schemaVersion: 1; id: string; title: string; revision: number; entryCanvasId: string;
  canvases: MeridianCanvas[]; nodes: MeridianNode[]; links: MeridianLink[];
  assets: MeridianAsset[]; snapshots: Record<string, SavedSnapshot>;
  dependencies: ExternalDependency[];
}
export interface Evaluation { values: Record<string, LogicValue>; errors: Record<string, string> }
/** A reusable, inert packing container. Insertion always creates an independent graph. */
export interface StoredModule {
  schemaVersion: 1; id: string; name: string; createdAt: string; rootId: string; project: MeridianProject;
}
export interface ValidationIssue { nodeId?: string; linkId?: string; message: string }
export interface PublicationPayload {
  format: 'atlas-meridian-publication'; version: 1; project: MeridianProject;
  publishedAt: string; missing: string[];
}
