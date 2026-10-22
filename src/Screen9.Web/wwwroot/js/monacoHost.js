// A Monaco editor instance in one element, loaded from the jsDelivr CDN on first use (Monaco has no
// single-file ESM build simple enough to self-host here). 'vb' is the closest built-in language to
// QBasic/QBS for keyword highlighting — not a real QBasic grammar.
const MONACO_VERSION = '0.52.2';

class MonacoHost {
    constructor(editor) { this.editor = editor; }
    getValue() { return this.editor.getValue(); }
    setValue(text) { this.editor.setValue(text); }
    setLanguage(language) { monaco.editor.setModelLanguage(this.editor.getModel(), language); }
    dispose() { this.editor.dispose(); }
}

function loadMonaco() {
    if (window.monaco) return Promise.resolve();
    if (window.__monacoLoading) return window.__monacoLoading;
    window.__monacoLoading = new Promise((resolve, reject) => {
        const loader = document.createElement('script');
        loader.src = `https://cdn.jsdelivr.net/npm/monaco-editor@${MONACO_VERSION}/min/vs/loader.js`;
        loader.onerror = reject;
        loader.onload = () => {
            window.require.config({ paths: { vs: `https://cdn.jsdelivr.net/npm/monaco-editor@${MONACO_VERSION}/min/vs` } });
            window.require(['vs/editor/editor.main'], resolve, reject);
        };
        document.head.appendChild(loader);
    });
    return window.__monacoLoading;
}

export async function createMonacoHost(element, initialValue) {
    await loadMonaco();
    const editor = monaco.editor.create(element, {
        value: initialValue,
        language: 'vb',
        theme: 'vs-dark',
        automaticLayout: true,
        minimap: { enabled: false },
        fontSize: 14,
    });
    return new MonacoHost(editor);
}
