import fs from 'node:fs/promises';
import path from 'node:path';
import ts from 'typescript';

const root = path.resolve(import.meta.dirname, '../..');
const destination = path.join(root, 'output/work-terminology-20261019/string-audit.json');
const strings = [];
async function walk(directory) {
  for (const item of await fs.readdir(directory, { withFileTypes: true })) {
    if (item.isDirectory() && !['bin', 'obj', 'node_modules'].includes(item.name)) await walk(path.join(directory, item.name));
    else if (item.isFile() && item.name.endsWith('.ts')) {
      const filename = path.join(directory, item.name), source = await fs.readFile(filename, 'utf8');
      const ast = ts.createSourceFile(filename, source, ts.ScriptTarget.Latest, true);
      function visit(node) {
        if ((ts.isStringLiteralLike(node) || ts.isTemplateHead(node) || ts.isTemplateMiddle(node) || ts.isTemplateTail(node)) && /\bcanvase?s?\b/i.test(node.text)) {
          const text = node.text, position = ast.getLineAndCharacterOfPosition(node.getStart(ast));
          const contract = /^(canvas|canvases|canvas-link|canvasId|entryCanvasId|canvasTarget|canvasTargets|targetCanvasId|sourceCanvasId)$/i.test(text)
            || /^(?:\.\/|\.\.\/|#|\.|\[|--|meridian[.:]|atlas[.:]|data-|canvas-|ms-canvas|mv-canvas)/i.test(text)
            || /^(?:canvasId|entryCanvasId|canvasTarget|canvasTargets)\b/.test(text);
          const browserPrimitive = text === 'canvas' && ts.isCallExpression(node.parent) && /createElement|querySelector|getContext/.test(node.parent.expression.getText(ast));
          const english = !contract && !browserPrimitive && (/[ \t]/.test(text) || /^[A-Z]/.test(text));
          strings.push({ file: path.relative(root, filename).replaceAll('\\', '/'), line: position.line + 1, column: position.character + 1,
            text, category: browserPrimitive ? 'browser-primitive' : contract ? 'compatibility-contract' : english ? 'review-prose' : 'technical-token',
            context: node.parent.getText(ast).slice(0, 360) });
        }
        ts.forEachChild(node, visit);
      }
      visit(ast);
    }
  }
}
await walk(path.join(root, 'src'));
await fs.mkdir(path.dirname(destination), { recursive: true });
await fs.writeFile(destination, JSON.stringify({ generatedAt: new Date().toISOString(), explanation: 'Source string audit only. Compatibility identifiers, CSS selectors and browser canvas primitives remain. Review-prose rows require human classification; this is not a blind replacement instruction.', strings }, null, 2));
for (const entry of strings.filter(entry => entry.category === 'review-prose')) console.log(`${entry.file}:${entry.line} ${JSON.stringify(entry.text)}`);
console.log(`Audited ${strings.length} retained strings; ${strings.filter(entry => entry.category === 'review-prose').length} prose candidates. ${destination}`);
