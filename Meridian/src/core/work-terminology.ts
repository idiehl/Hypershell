import type { NodeKind } from './types.ts';

/** Friendly product names; serialized kind values remain compatible with existing Works. */
export function nodeKindLabel(kind: NodeKind): string {
  return kind === 'canvas' || kind === 'canvas-link' ? 'Work' : kind;
}

const LIBRARY_PROJECT_ID = 'c9f781c1-124a-433e-a1d6-37dd1d8a7580';
const LIBRARY_DEFAULT_IDS = new Set([LIBRARY_PROJECT_ID, 'c9f781c1-124a-433e-a1d6-37dd1d8a7581', 'c9f781c1-124a-433e-a1d6-37dd1d8a7582', 'c9f781c1-124a-433e-a1d6-37dd1d8a7583']);
const LIBRARY_DEFAULT_TEXT: Readonly<Record<string, string>> = {
  'Canvas library': 'Work Library',
  'Canvas Library': 'Work Library',
  'Your canvas library': 'Your Work Library',
  'Your Canvas Library': 'Your Work Library',
  'Select a canvas to begin.': 'Select a Work to begin.',
  'Arrange canvases into chapters and sections. Expand a node to explore its contents.': 'Arrange Works into chapters and sections. Expand a node to explore its contents.',
  'A canvas for your ideas and the canvases they connect. Expand any node to explore its contents.': 'A Work for your ideas and the Works they connect. Expand any node to explore its contents.',
};

/** Render historical factory text only in reserved library entities. Never rewrite saved titles, Tags, prose or IDs. */
export function workLibraryDisplayText(projectId: string, entityId: string, text: string): string {
  return projectId === LIBRARY_PROJECT_ID && LIBRARY_DEFAULT_IDS.has(entityId) && Object.hasOwn(LIBRARY_DEFAULT_TEXT, text)
    ? LIBRARY_DEFAULT_TEXT[text] ?? text : text;
}
