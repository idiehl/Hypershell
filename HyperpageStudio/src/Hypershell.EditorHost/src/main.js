import * as monaco from "monaco-editor";
import EditorWorker from "monaco-editor/editor/editor.worker?worker";
import JsonWorker from "monaco-editor/language/json/json.worker?worker";
import CssWorker from "monaco-editor/language/css/css.worker?worker";
import HtmlWorker from "monaco-editor/language/html/html.worker?worker";
import TypeScriptWorker from "monaco-editor/language/typescript/ts.worker?worker";
import "./style.css";

self.MonacoEnvironment = {
  getWorker(_moduleId, label) {
    if (label === "json") {
      return new JsonWorker();
    }

    if (label === "css" || label === "scss" || label === "less") {
      return new CssWorker();
    }

    if (label === "html" || label === "handlebars" || label === "razor") {
      return new HtmlWorker();
    }

    if (label === "typescript" || label === "javascript") {
      return new TypeScriptWorker();
    }

    return new EditorWorker();
  }
};

const host = window.chrome?.webview;
let suppressChanges = false;
let viewStateTimer = 0;

monaco.editor.defineTheme("hypershell-dark", {
  base: "vs-dark",
  inherit: true,
  rules: [
    { token: "comment", foreground: "8B949E", fontStyle: "italic" },
    { token: "keyword", foreground: "FF9F43" },
    { token: "string", foreground: "A5D6FF" },
    { token: "number", foreground: "79C0FF" },
    { token: "type", foreground: "D2A8FF" }
  ],
  colors: {
    "editor.background": "#0D1117",
    "editor.foreground": "#E6EDF3",
    "editorLineNumber.foreground": "#484F58",
    "editorLineNumber.activeForeground": "#FF9F43",
    "editorCursor.foreground": "#FF9F43",
    "editor.selectionBackground": "#264F78AA",
    "editor.inactiveSelectionBackground": "#264F7855",
    "editor.lineHighlightBackground": "#161B2266",
    "editorIndentGuide.background1": "#30363D",
    "editorIndentGuide.activeBackground1": "#58A6FF",
    "editorWidget.background": "#161B22",
    "editorWidget.border": "#30363D"
  }
});

const model = monaco.editor.createModel("", "plaintext", monaco.Uri.parse("inmemory://hypershell/untitled"));
const editor = monaco.editor.create(document.getElementById("editor"), {
  model,
  theme: "hypershell-dark",
  automaticLayout: true,
  accessibilitySupport: "auto",
  fontFamily: "'JetBrains Mono', Consolas, monospace",
  fontSize: 14,
  lineHeight: 21,
  minimap: { enabled: true },
  padding: { top: 12, bottom: 16 },
  smoothScrolling: true,
  tabSize: 4,
  insertSpaces: true,
  renderWhitespace: "selection",
  bracketPairColorization: { enabled: true },
  guides: { bracketPairs: true, indentation: true },
  scrollBeyondLastLine: false,
  wordWrap: "off",
  fixedOverflowWidgets: true
});

function post(type, payload = {}) {
  host?.postMessage({ type, ...payload });
}

function publishViewState() {
  window.clearTimeout(viewStateTimer);
  viewStateTimer = window.setTimeout(() => {
    post("viewState", { viewState: editor.saveViewState() });
  }, 120);
}

editor.onDidChangeModelContent(() => {
  if (!suppressChanges) {
    post("change", { text: model.getValue() });
  }

  publishViewState();
});

editor.onDidChangeCursorPosition((event) => {
  post("cursor", {
    line: event.position.lineNumber,
    column: event.position.column
  });
  publishViewState();
});

editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
  post("save");
});

editor.addCommand(
  monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KeyS,
  () => post("saveAs")
);

editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyN, () => {
  post("newDocument");
});

editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyO, () => {
  post("openDocument");
});

editor.addCommand(
  monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KeyF,
  () => post("formatRequested")
);

editor.addCommand(
  monaco.KeyMod.Shift | monaco.KeyMod.Alt | monaco.KeyCode.KeyF,
  () => post("formatRequested")
);

editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyF, () => {
  executeHostCommand("find");
});

editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyH, () => {
  executeHostCommand("replace");
});

editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyG, () => {
  executeHostCommand("goToLine");
});

editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyB, () => {
  if (model.getLanguageId() === "markdown") {
    applyMarkdownFormat("bold");
  }
});

editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyI, () => {
  if (model.getLanguageId() === "markdown") {
    applyMarkdownFormat("italic");
  }
});

function normalizeDocumentWhitespace() {
  const endOfLine = model.getEOL();
  const normalized = model
    .getLinesContent()
    .map((line) => model.normalizeIndentation(line.replace(/[\t ]+$/u, "")))
    .join(endOfLine);
  if (normalized !== model.getValue()) {
    editor.executeEdits("hypershell.format", [
      {
        range: model.getFullModelRange(),
        text: normalized,
        forceMoveMarkers: true
      }
    ]);
    return true;
  }

  return false;
}

async function formatDocument() {
  const action = editor.getAction("editor.action.formatDocument");
  if (action?.isSupported()) {
    await action.run();
    editor.focus();
    return {
      success: true,
      message: `Formatted with the registered ${model.getLanguageId()} formatter.`
    };
  } else {
    const changed = normalizeDocumentWhitespace();
    editor.focus();
    return {
      success: true,
      message: changed
        ? "No language formatter is registered; safe whitespace normalization was applied."
        : "No language formatter is registered; the document already matches safe whitespace rules."
    };
  }
}

async function executeHostCommand(command, requestId = "") {
  try {
    let result;
    switch (command) {
      case "find":
        result = await runEditorAction("actions.find", "Find opened.");
        break;
      case "replace":
        result = await runEditorAction(
          "editor.action.startFindReplaceAction",
          "Find and Replace opened."
        );
        break;
      case "goToLine":
        result = await runEditorAction("editor.action.gotoLine", "Go to Line opened.");
        break;
      case "format":
        result = await formatDocument();
        break;
      default:
        result = { success: false, message: "The requested editor action is not supported." };
        break;
    }

    post("commandResult", {
      requestId,
      command,
      success: result.success,
      message: result.message
    });
  } catch (error) {
    post("commandResult", {
      requestId,
      command,
      success: false,
      message: error instanceof Error
        ? `Editor action failed: ${error.message}`
        : "The editor action failed."
    });
  }
}

async function runEditorAction(actionId, successMessage) {
  const action = editor.getAction(actionId);
  if (!action?.isSupported()) {
    return {
      success: false,
      message: "This editor action is not available for the active document."
    };
  }

  await action.run();
  return { success: true, message: successMessage };
}

function applyMarkdownFormat(style) {
  if (model.getLanguageId() !== "markdown") {
    return {
      success: false,
      message: "Markdown formatting is available when the document language is Markdown."
    };
  }

  const selection = editor.getSelection();
  if (!selection) {
    return { success: false, message: "The editor selection is unavailable." };
  }

  const selectedText = model.getValueInRange(selection);
  const wrap = (prefix, suffix, placeholder) => {
    editor.executeEdits("hypershell.markdown", [
      {
        range: selection,
        text: `${prefix}${selectedText || placeholder}${suffix}`,
        forceMoveMarkers: true
      }
    ]);
  };
  const replaceSelectedLines = (transform) => {
    const startLine = selection.startLineNumber;
    const endLine = selection.endColumn === 1 && selection.endLineNumber > startLine
      ? selection.endLineNumber - 1
      : selection.endLineNumber;
    const lines = [];
    for (let lineNumber = startLine; lineNumber <= endLine; lineNumber += 1) {
      lines.push(model.getLineContent(lineNumber));
    }

    const range = new monaco.Range(
      startLine,
      1,
      endLine,
      model.getLineMaxColumn(endLine)
    );
    editor.executeEdits("hypershell.markdown", [
      {
        range,
        text: transform(lines).join(model.getEOL()),
        forceMoveMarkers: true
      }
    ]);
  };
  const removeBlockPrefix = (line) => line.replace(
    /^(\s*)(?:(?:#{1,6}|[-+*]|>|\d+[.)])\s+)/u,
    "$1"
  );

  switch (style) {
    case "bold":
      wrap("**", "**", "bold text");
      break;
    case "italic":
      wrap("_", "_", "italic text");
      break;
    case "strikethrough":
      wrap("~~", "~~", "struck text");
      break;
    case "inlineCode":
      wrap("`", "`", "code");
      break;
    case "link":
      wrap("[", "](https://)", "link text");
      break;
    case "codeBlock":
      wrap(`\`\`\`${model.getEOL()}`, `${model.getEOL()}\`\`\``, "code");
      break;
    case "heading1":
    case "heading2":
    case "heading3": {
      const level = Number(style.at(-1));
      replaceSelectedLines((lines) => lines.map((line) =>
        `${"#".repeat(level)} ${removeBlockPrefix(line).trimStart()}`
      ));
      break;
    }
    case "paragraph":
      replaceSelectedLines((lines) => lines.map(removeBlockPrefix));
      break;
    case "bulletList":
      replaceSelectedLines((lines) => lines.map((line) => {
        const indent = line.match(/^\s*/u)?.[0] ?? "";
        return `${indent}- ${removeBlockPrefix(line).trimStart()}`;
      }));
      break;
    case "numberedList":
      replaceSelectedLines((lines) => lines.map((line, index) => {
        const indent = line.match(/^\s*/u)?.[0] ?? "";
        return `${indent}${index + 1}. ${removeBlockPrefix(line).trimStart()}`;
      }));
      break;
    case "quote":
      replaceSelectedLines((lines) => lines.map((line) => {
        const indent = line.match(/^\s*/u)?.[0] ?? "";
        return `${indent}> ${removeBlockPrefix(line).trimStart()}`;
      }));
      break;
    case "horizontalRule":
      editor.executeEdits("hypershell.markdown", [
        {
          range: selection,
          text: `${selectedText}${selectedText ? model.getEOL() : ""}---${model.getEOL()}`,
          forceMoveMarkers: true
        }
      ]);
      break;
    default:
      return { success: false, message: "The requested Markdown style is not supported." };
  }

  editor.focus();
  return { success: true, message: "Markdown formatting applied." };
}

function applyState(state) {
  suppressChanges = true;
  try {
    const text = typeof state.text === "string" ? state.text : "";
    if (model.getValue() !== text) {
      model.setValue(text);
    }

    const language = typeof state.language === "string" && state.language
      ? state.language
      : "plaintext";
    if (model.getLanguageId() !== language) {
      monaco.editor.setModelLanguage(model, language);
    }

    const tabSize = Number.isInteger(state.tabSize)
      ? Math.min(8, Math.max(1, state.tabSize))
      : 4;
    const insertSpaces = state.insertSpaces !== false;
    if (state.detectIndentation === true && text.length > 0) {
      model.detectIndentation(insertSpaces, tabSize);
    } else {
      model.updateOptions({
        tabSize,
        insertSpaces,
        trimAutoWhitespace: true
      });
    }

    editor.updateOptions({
      readOnly: state.readOnly === true,
      autoIndent: "full",
      wordWrap: state.wordWrap === true ? "on" : "off",
      minimap: { enabled: state.minimap !== false },
      lineNumbers: state.lineNumbers === false ? "off" : "on",
      bracketPairColorization: { enabled: state.bracketPairGuides !== false },
      guides: {
        bracketPairs: state.bracketPairGuides !== false,
        indentation: true
      },
      formatOnType: state.formatOnType !== false,
      formatOnPaste: state.formatOnPaste !== false,
      fontFamily: typeof state.fontFamily === "string" && state.fontFamily
        ? `'${state.fontFamily.replaceAll("'", "\\'")}', Consolas, monospace`
        : "'JetBrains Mono', Consolas, monospace",
      fontSize: Number.isInteger(state.fontSize)
        ? Math.min(32, Math.max(10, state.fontSize))
        : 14
    });
    if (state.viewState) {
      editor.restoreViewState(state.viewState);
    }
  } finally {
    suppressChanges = false;
  }

  editor.focus();
}

function insertText(text) {
  if (typeof text !== "string" || !text) {
    return;
  }

  const selection = editor.getSelection();
  editor.executeEdits("hypershell", [{ range: selection, text, forceMoveMarkers: true }]);
  editor.focus();
}

host?.addEventListener("message", (event) => {
  const message = event.data ?? {};
  switch (message.type) {
    case "setState":
      applyState(message);
      break;
    case "focus":
      editor.focus();
      break;
    case "insertText":
      insertText(message.text);
      break;
    case "format":
      executeHostCommand("format", message.requestId);
      break;
    case "command":
      executeHostCommand(message.command, message.requestId);
      break;
    case "markdownFormat": {
      const result = applyMarkdownFormat(message.style);
      post("commandResult", {
        requestId: message.requestId,
        command: `markdownFormat:${message.style ?? ""}`,
        success: result.success,
        message: result.message
      });
      break;
    }
  }
});

window.addEventListener("beforeunload", () => {
  post("viewState", { viewState: editor.saveViewState() });
  model.dispose();
  editor.dispose();
});

post("ready");
