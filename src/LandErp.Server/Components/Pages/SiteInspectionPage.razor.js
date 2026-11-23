// Each document owns a branch. Acknowledgements remove only the exact revisions read.
const writer = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`;
const ownKey = key => `${key}.draft.${writer}`;
const read = key => { try { return JSON.parse(localStorage.getItem(key)); } catch { return null; } };
let listening = false;
export function loadAll(key, legacyKey = null) {
    const result = [];
    try {
    for (let i = 0; i < localStorage.length; i++) {
        const name = localStorage.key(i);
        if (name?.startsWith(`${key}.draft.`)) {
            const value = read(name);
            if (value?.inspectionId && Array.isArray(value.answers)) result.push(value);
        }
    }
    for (const name of [key, legacyKey].filter(Boolean)) {
        const value = read(name);
        if (value?.inspectionId && Array.isArray(value.answers))
            result.push({...value, revision: `legacy:${name}:${JSON.stringify(value)}`});
    }
    } catch { /* Storage may be unavailable; keep online editing usable. */ }
    return result;
}
export function load(key) { return read(ownKey(key)) ?? read(key); }
export function save(key, value) {
    const revision = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`;
    localStorage.setItem(ownKey(key), JSON.stringify({...value, revision}));
    const root = document.querySelector('[data-inspection-root="true"]');
    if (root?.inspectionKey === key) root.inspectionSaved = true;
    return revision;
}
export function isSaved() { return document.querySelector('[data-inspection-root="true"]')?.inspectionSaved === true; }
export function acknowledge(key, revisions, legacyKey = null) {
    const names = [];
    for (let i = 0; i < localStorage.length; i++) {
        const name = localStorage.key(i);
        if (name?.startsWith(`${key}.draft.`)) names.push(name);
    }
    for (const name of [key, legacyKey].filter(Boolean)) names.push(name);
    for (const name of names) {
        const value = read(name);
        if (!value) continue;
        const revision = name.startsWith(`${key}.draft.`) ? value.revision : `legacy:${name}:${JSON.stringify(value)}`;
        if (revisions.includes(revision)) localStorage.removeItem(name);
    }
}
export function remove(key) { localStorage.removeItem(ownKey(key)); }
export function downloadDraft(name, drafts) {
    const url = URL.createObjectURL(new Blob([JSON.stringify(drafts, null, 2)], {type:'application/json'}));
    const link = document.createElement('a');link.href=url;link.download=name;link.click();
    setTimeout(()=>URL.revokeObjectURL(url),1000);
}
export function attach(key, initial) {
    const root = document.querySelector('[data-inspection-root="true"]');
    if (!root) return;
    root.inspectionSnapshot = initial;
    root.inspectionKey = key;
    root.dataset.inspectionDraftReady = 'true';
    if (listening) return;
    listening = true;
    const update = event => {
        const activeRoot = document.querySelector('[data-inspection-root="true"]');
        if (!activeRoot?.inspectionSnapshot) return;
        const target = event.target.closest('[data-inspection-answer],[data-inspection-note],[data-inspection-not-checked],[data-inspection-conclusion],[data-inspection-decision]');
        if (!target || target.disabled || target.closest('fieldset:disabled')) return;
        const current = activeRoot.inspectionSnapshot;
        const stored = load(activeRoot.inspectionKey);
        // Answer-only saves can change item versions without changing the inspection version.
        const sameSnapshot = stored?.inspectionId === current.inspectionId && stored?.version === current.version
            && stored.answers?.length === current.answers.length
            && current.answers.every(item => stored.answers.some(old => old.id === item.id && old.version === item.version));
        const draft = sameSnapshot ? stored : structuredClone(current);
        if (target.dataset.inspectionConclusion) draft.conclusion = target.value;
        else if (target.dataset.inspectionDecision) draft.decision = target.value;
        else {
            const id = target.dataset.inspectionAnswer || target.dataset.inspectionNote || target.dataset.inspectionNotChecked;
            const item = draft.answers.find(answer => answer.id === id);
            if (!item) return;
            if (target.dataset.inspectionNote) item.note = target.value;
            else if (target.dataset.inspectionNotChecked) { item.answer = ''; item.status = 2; }
            else { item.answer = target.dataset.inspectionValue ?? target.value; item.status = item.answer ? 1 : 0; }
        }
        try { save(activeRoot.inspectionKey, draft); } catch { activeRoot.inspectionSaved = false; }
    };
    // Conclusion inputs are in a modal outside main; offline capture must cover them too.
    document.addEventListener('input', update);
    document.addEventListener('change', update);
    document.addEventListener('click', update);
}
