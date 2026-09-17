// The snapshot is updated after each render, including after a server version change.
export function load(key) {
    try { return JSON.parse(localStorage.getItem(key)); } catch { return null; }
}
export function save(key, value) { localStorage.setItem(key, JSON.stringify(value)); }
export function remove(key) { localStorage.removeItem(key); }
export function attach(key, initial) {
    const root = document.querySelector('[data-inspection-root="true"]');
    if (!root) return;
    root.inspectionSnapshot = initial;
    root.inspectionKey = key;
    if (root.dataset.caseDraftAttached) return;
    root.dataset.caseDraftAttached = 'true';
    const update = event => {
        const target = event.target.closest('[data-inspection-answer],[data-inspection-note],[data-inspection-not-checked]');
        if (!target || target.disabled) return;
        const current = root.inspectionSnapshot;
        const stored = load(root.inspectionKey);
        const draft = stored?.inspectionId === current.inspectionId && stored?.version === current.version ? stored : structuredClone(current);
        const id = target.dataset.inspectionAnswer || target.dataset.inspectionNote || target.dataset.inspectionNotChecked;
        const item = draft.answers.find(answer => answer.id === id);
        if (!item) return;
        if (target.dataset.inspectionNote) item.note = target.value;
        else if (target.dataset.inspectionNotChecked) { item.answer = ''; item.status = 2; }
        else { item.answer = target.dataset.inspectionValue ?? target.value; item.status = item.answer ? 1 : 0; }
        try { save(root.inspectionKey, draft); } catch { /* Online form reports storage availability separately. */ }
    };
    root.addEventListener('input', update);
    root.addEventListener('change', update);
    root.addEventListener('click', update);
}
