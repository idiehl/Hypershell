import { defineConfig } from "vite";
import { copyFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { resolve } from "node:path";

const editorHostDirectory = fileURLToPath(new URL(".", import.meta.url));
const outputDirectory = resolve(editorHostDirectory, "../Hypershell/Assets/EditorHost");

export default defineConfig({
  base: "./",
  plugins: [
    {
      name: "copy-monaco-license",
      closeBundle() {
        copyFileSync(
          resolve(editorHostDirectory, "node_modules/monaco-editor/LICENSE"),
          resolve(outputDirectory, "LICENSE.monaco-editor.txt")
        );
      }
    }
  ],
  build: {
    outDir: outputDirectory,
    emptyOutDir: true,
    target: "es2022",
    sourcemap: false,
    assetsInlineLimit: 0
  }
});
