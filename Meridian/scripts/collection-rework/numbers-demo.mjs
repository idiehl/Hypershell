// Declarative authoring recipe for the new working-system teaching canvas.
export function numbersDemo(entryCanvasId, entryRootId) {
  const canvas = 'numbers-demo';
  const ink = '#E8F4F0', muted = '#AAD1C4', mint = '#75DAC4', amber = '#E8B967';
  const card = { fill: '#1B3541', border: '#365D69', textColor: ink, accent: mint, titleSize: 23, fontSize: 18, radius: 14 };
  const label = { variant: 'label', textColor: ink, titleSize: 44, fontSize: 22 };
  const node = (key, kind, title, x, y, width, height, extra = {}) => ({ key, canvas, kind, title, x, y, width, height, style: card, ...extra });
  const link = (key, source, target, targetPort = 0, kind = 'numeric', extra = {}) => ({ key, canvas, source, target, kind, relation: 'causal', targetPort, sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: .5 }, ...extra });
  return {
    canvases: [{ key: canvas, title: 'Numbers, decisions and junctions', style: { background: '#10232D', gridVisible: false } }],
    nodes: [
      { key: 'numbers-demo-launch', canvas: entryCanvasId, parent: entryRootId, kind: 'canvas-link', title: 'Numbers, decisions and junctions', target: canvas, text: 'A working calculation with typed connections, shared routing and a visible result.', x: 710, y: 800, width: 480, height: 190, style: card },
      node('demo-heading', 'text', 'From numbers to a decision', 50, 40, 1280, 95, { style: label, text: '' }),
      node('demo-intro', 'text', 'Change a value. Follow the result.', 55, 150, 1330, 110, { style: { ...label, titleSize: 26, textColor: muted }, text: 'Run the canvas, then edit price or quantity. A routing junction shares one subtotal; operation joints calculate and compare.' }),
      node('demo-back', 'canvas-link', 'First connections', 1550, 50, 290, 150, { target: entryRootId, text: 'Return to the starting canvas.' }),
      node('demo-system', 'container', 'A small order · calculation system', 50, 300, 1390, 1090, { text: 'Collapse this container: the final result remains visible beside it.', collapsed: false }),
      node('demo-price', 'number', 'Unit price', 85, 465, 225, 155, { parent: 'demo-system', value: 12, exposed: true }),
      node('demo-quantity', 'number', 'Quantity', 85, 695, 225, 155, { parent: 'demo-system', value: 3, exposed: true }),
      node('demo-multiply', 'joint', 'Price × quantity', 385, 560, 260, 150, { parent: 'demo-system', operation: 'MULTIPLY' }),
      node('demo-share', 'junction', 'Share the subtotal', 730, 615, 44, 44, { parent: 'demo-system', valueType: 'number' }),
      node('demo-subtotal', 'output', 'Subtotal', 850, 445, 270, 145, { parent: 'demo-system', valueType: 'number' }),
      node('demo-fee', 'number', 'Handling charge', 715, 900, 235, 155, { parent: 'demo-system', value: 4, exposed: true }),
      node('demo-add', 'joint', 'Subtotal + charge', 1000, 710, 300, 150, { parent: 'demo-system', operation: 'ADD' }),
      node('demo-switch', 'switch', 'Order enabled', 1040, 1015, 280, 150, { parent: 'demo-system', valueType: 'number', closed: true, exposed: true }),
      node('demo-total', 'output', 'Order total', 1510, 440, 315, 155, { valueType: 'number' }),
      node('demo-threshold', 'number', 'Review threshold', 1510, 710, 315, 155, { value: 40, exposed: true }),
      node('demo-compare', 'joint', 'Total > threshold', 1510, 1015, 315, 150, { operation: 'GREATER' }),
      node('demo-result', 'output', 'Needs review?', 1510, 1265, 315, 150),
      node('demo-notes', 'container', 'How the connections work', 50, 1470, 1390, 520, { text: 'Expand for the difference between operations, routing and undefined output.', collapsed: true }),
      node('demo-joint-note', 'text', 'An operation joint changes a value', 85, 1645, 400, 270, { parent: 'demo-notes', text: 'MULTIPLY, ADD and GREATER are explicit operations. GREATER receives numbers and produces a Boolean decision. Changing operands updates the result while the canvas is running.' }),
      node('demo-route-note', 'text', 'A junction shares a route', 525, 1645, 400, 270, { parent: 'demo-notes', text: 'The small routing junction forwards the subtotal to two destinations. It does not add the signals together. Crossing lines alone never connect them.' }),
      node('demo-open-note', 'text', 'An open switch makes the result undefined', 965, 1645, 440, 270, { parent: 'demo-notes', text: 'Open Order enabled. The total and the review decision clear to undefined; they do not retain an old answer or become zero. The independent subtotal remains available.' }),
    ],
    links: [
      link('demo-price-multiply', 'demo-price', 'demo-multiply', 0),
      link('demo-quantity-multiply', 'demo-quantity', 'demo-multiply', 1),
      link('demo-multiply-share', 'demo-multiply', 'demo-share'),
      link('demo-share-subtotal', 'demo-share', 'demo-subtotal', 0, 'numeric', { points: [{ x: 790, y: 637 }, { x: 790, y: 517 }] }),
      link('demo-share-add', 'demo-share', 'demo-add', 0, 'numeric', { points: [{ x: 810, y: 637 }, { x: 810, y: 785 }] }),
      link('demo-fee-add', 'demo-fee', 'demo-add', 1),
      link('demo-add-switch', 'demo-add', 'demo-switch', 0, 'numeric', { sourceAnchor: { x: .5, y: 1 }, targetAnchor: { x: .5, y: 0 } }),
      link('demo-switch-total', 'demo-switch', 'demo-total', 0, 'numeric', { points: [{ x: 1460, y: 1090 }, { x: 1460, y: 517 }] }),
      link('demo-total-compare', 'demo-total', 'demo-compare', 0, 'numeric', { sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 1, y: .25 }, points: [{ x: 1885, y: 517 }, { x: 1885, y: 1052 }] }),
      link('demo-threshold-compare', 'demo-threshold', 'demo-compare', 1, 'numeric', { sourceAnchor: { x: .5, y: 1 }, targetAnchor: { x: .5, y: 0 } }),
      link('demo-compare-result', 'demo-compare', 'demo-result', 0, 'functional', { sourceAnchor: { x: .5, y: 1 }, targetAnchor: { x: .5, y: 0 } }),
    ],
  };
}
