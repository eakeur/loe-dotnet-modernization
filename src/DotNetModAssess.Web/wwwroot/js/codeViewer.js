// JS interop module backing Components/Workspace/FileViewer.razor.
//
// The Monaco Editor (the actual editor VS Code itself is built on) is loaded via its AMD loader,
// CDN-included as a <script> tag in App.razor (same pattern as Cytoscape.js: pinned version, not
// @latest). That loader script only defines a global `require()` - the editor itself is fetched
// lazily the first time init() below actually needs it, not eagerly on every page load.

let monacoReadyPromise = null;

function ensureMonacoLoaded() {
    if (monacoReadyPromise) {
        return monacoReadyPromise;
    }

    monacoReadyPromise = new Promise((resolve, reject) => {
        try {
            window.require.config({
                paths: { vs: "https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min/vs" }
            });
            window.require(["vs/editor/editor.main"], () => resolve(window.monaco), reject);
        } catch (err) {
            reject(err);
        }
    });

    return monacoReadyPromise;
}

// Creates a read-only Monaco instance showing `content`, in `language`'s syntax highlighting,
// scrolled to and highlighting `lineNumber` (1-based) if provided. Returns a small wrapper object
// (dispose/setContent) - the same "hand .NET a plain object, not the raw editor" convention
// dependencyGraph.js already established for Cytoscape.
export async function init(container, content, language, lineNumber) {
    const monaco = await ensureMonacoLoaded();

    const editor = monaco.editor.create(container, {
        value: content,
        language: language,
        readOnly: true,
        theme: "vs-dark",
        automaticLayout: true,
        minimap: { enabled: false },
        renderLineHighlight: "none",
        scrollBeyondLastLine: false,
        fontSize: 13,
        wordWrap: "off",
    });

    let decorationIds = [];

    function highlightLine(target) {
        editor.deltaDecorations(decorationIds, []);
        decorationIds = [];

        if (!target || target < 1) {
            return;
        }

        decorationIds = editor.deltaDecorations([], [
            {
                range: new monaco.Range(target, 1, target, 1),
                options: {
                    isWholeLine: true,
                    className: "vsc-monaco-highlighted-line",
                    linesDecorationsClassName: "vsc-monaco-highlighted-line-gutter"
                }
            }
        ]);

        editor.revealLineInCenter(target);
        editor.setPosition({ lineNumber: target, column: 1 });
    }

    highlightLine(lineNumber);
    editor.focus();

    return {
        highlightLine,
        dispose() {
            editor.dispose();
        }
    };
}
