import type { MeridianProject as Project, MeridianNode as Node, MeridianLink as Link } from './types.ts';
import { addCanvas, addNode, commitSnapshots, newProject } from './model.ts';

export function createDemoProject(): Project {
  let project = newProject('Meridian · First connections');
  const research = project.canvases[0]!;
  research.title = 'Start here';
  project.nodes.find(node => node.id === research.rootId)!.title = research.title;
  const lab = addCanvas(project, 'Logic lab');
  const place = (node: Node, x: number, y: number, width = 230, height = 120): Node => Object.assign(node, { x, y, width, height });
  const connect = (source: Node, target: Node, kind: Link['kind'], targetPort = 0): void => {
    project.links.push({ id: crypto.randomUUID(), canvasId: source.canvasId, sourceId: source.id, targetId: target.id, kind, relation: 'causal', targetPort });
  };
  const intro = place(addNode(project, research.id, 'text', 'One Work. Many ways to connect.'), 70, 50, 560, 140);
  intro.text = 'Arrange ideas, reveal the detail inside them, and connect a working system. Open Logic lab to try the Boolean circuit. Your project stays yours; Publish creates an interactive HTML you can open offline.';
  const collection = place(addNode(project, research.id, 'container', 'A small research collection'), 70, 250, 820, 330);
  const note = place(addNode(project, research.id, 'text', 'Keep the original material'), 100, 310, 270, 180);
  note.parentId = collection.id;
  note.text = 'Text, images and source documents share the same node model. Collapse this collection to hide its children. The neighbouring nodes stay visible.';
  const imageId = crypto.randomUUID(), documentId = crypto.randomUUID();
  project.assets.push({
    id: imageId, name: 'meridian-checker.png', mime: 'image/png',
    base64: 'iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAIAAAD8GO2jAAAAQklEQVR4nGOQNfHEivT3z8KKSFXPMGrBqAVDwAJqGYRL/agFoxYMBQuoZRAu9aMWjFowFCyglkG41I9aMGrBELAAAKIERD2HZR8fAAAAAElFTkSuQmCC',
  });
  const sourceText = 'Atlas Meridian sample source\n\nThis original TXT attachment travels inside the project and published HTML.\nThe checker image is a losslessly preserved PNG fixture.\n';
  project.assets.push({ id: documentId, name: 'source-notes.txt', mime: 'text/plain', base64: btoa(sourceText) });
  const image = place(addNode(project, research.id, 'image', 'A bundled image'), 400, 310, 200, 180);
  image.parentId = collection.id; image.assetId = imageId;
  const document = place(addNode(project, research.id, 'document', 'Original source notes'), 630, 330, 220, 130);
  document.parentId = collection.id; document.assetId = documentId;
  const source = place(addNode(project, research.id, 'input', 'Evidence verified'), 950, 280, 230, 120);
  source.value = true; source.exposed = true;
  const sourceExplanation = place(addNode(project, research.id, 'text', 'Saved references'), 950, 440, 280, 150);
  sourceExplanation.text = 'Logic lab observes this value through a saved reference. Edit it here, then save to refresh the other Work. Unsaved changes stay in this Work.';
  const launch = place(addNode(project, research.id, 'canvas-link', 'Open Logic lab'), 710, 65, 290, 135);
  launch.targetId = lab.rootId;
  connect(intro, launch, 'visual');
  connect(note, document, 'visual');

  // Establish a source save before wiring its snapshot into the other canvas.
  project = commitSnapshots(project);
  const explanation = place(addNode(project, lab.id, 'text', 'A decision you can inspect'), 70, 45, 740, 130);
  explanation.text = 'The AND joint is true when both inputs are true. Toggle Ready to proceed; open the switch to make the result undefined. Collapse Boolean decision: its children hide, while the sibling Decision display stays visible and keeps its value.';
  const system = place(addNode(project, lab.id, 'container', 'Boolean decision'), 70, 215, 905, 395);
  system.rules = { nodeKinds: ['input', 'switch', 'joint', 'output', 'reference'], linkKinds: ['functional'], operations: ['AND', 'OR', 'NOT', 'NAND', 'NOR', 'XOR', 'XNOR'] };
  const ready = place(addNode(project, lab.id, 'input', 'Ready to proceed'), 105, 275, 225, 110);
  ready.parentId = system.id; ready.value = true;
  const reference = place(addNode(project, lab.id, 'reference', 'Evidence · saved'), 105, 430, 225, 115);
  reference.parentId = system.id; reference.targetId = source.id;
  const toggle = place(addNode(project, lab.id, 'switch', 'Evidence connection'), 420, 430, 230, 115);
  toggle.parentId = system.id;
  const gate = place(addNode(project, lab.id, 'joint', 'Both conditions'), 745, 315, 195, 130);
  gate.parentId = system.id; gate.operation = 'AND';
  const output = place(addNode(project, lab.id, 'output', 'Decision'), 1020, 315, 205, 130);
  connect(ready, gate, 'functional', 0);
  connect(reference, toggle, 'functional');
  connect(toggle, gate, 'functional', 1);
  connect(gate, output, 'functional');
  const returnLink = place(addNode(project, lab.id, 'canvas-link', 'Back to Start here'), 950, 65, 280, 115);
  returnLink.targetId = research.rootId;
  return commitSnapshots(project);
}
