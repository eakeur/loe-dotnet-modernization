// Thin localStorage wrapper for cached solution analysis results (see AnalysisCacheService.cs).
// Kept as plain functions rather than a stateful init()/dispose() module (unlike dependencyGraph.js
// / codeViewer.js) since get/set/remove on localStorage need no persistent JS-side instance.

export function getItem(key) {
    return window.localStorage.getItem(key);
}

export function setItem(key, value) {
    window.localStorage.setItem(key, value);
}

export function removeItem(key) {
    window.localStorage.removeItem(key);
}
