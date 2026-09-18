window.LandErpUi = {
    loadShellPreferences: function () {
        try {
            const value = JSON.parse(localStorage.getItem("landerp.ui.shell.v1"));
            return { compact: value?.compact === true, sidebarCollapsed: value?.sidebarCollapsed === true };
        } catch {
            return { compact: false, sidebarCollapsed: false };
        }
    },
    saveShellPreferences: function (value) {
        // Whitelist UI booleans. Private mode/quota limits must not break navigation.
        try {
            localStorage.setItem("landerp.ui.shell.v1", JSON.stringify({
                compact: value?.compact === true,
                sidebarCollapsed: value?.sidebarCollapsed === true
            }));
        } catch { }
    },
    open: function (dialog) {
        if (!dialog.open) dialog.showModal();
        dialog.addEventListener("cancel", function (event) { event.preventDefault(); });
    }
};

window.landErpInspection = {
    load: function (key) {
        const value = localStorage.getItem(key);
        if (!value) return null;
        try { return JSON.parse(value); } catch { localStorage.removeItem(key); return null; }
    },
    save: function (key, value) { localStorage.setItem(key, JSON.stringify(value)); },
    attach: function (key, initial) {
        const root = document.querySelector("[data-inspection-root='true']");
        if (!root || !initial) return;
        const version = String(initial.version ?? "");
        const inspectionId = String(initial.inspectionId ?? "");
        if (root.dataset.draftAttached === "true"
            && root.dataset.draftVersion === version
            && root.dataset.draftInspectionId === inspectionId) return;
        if (root._landErpInspectionUpdate) {
            root.removeEventListener("input", root._landErpInspectionUpdate);
            root.removeEventListener("change", root._landErpInspectionUpdate);
            root.removeEventListener("click", root._landErpInspectionUpdate);
        }
        root.dataset.draftAttached = "true";
        root.dataset.draftVersion = version;
        root.dataset.draftInspectionId = inspectionId;
        const update = function (event) {
            const target = event.target;
            let draft = initial;
            try {
                const saved = JSON.parse(localStorage.getItem(key));
                if (saved && saved.inspectionId === initial.inspectionId && saved.version === initial.version) draft = saved;
            } catch { localStorage.removeItem(key); }
            const itemId = target.dataset.inspectionAnswer || target.dataset.inspectionNote || target.dataset.inspectionNotChecked;
            const item = itemId ? draft.answers.find(value => value.id === itemId) : null;
            if (target.dataset.inspectionConclusion) draft.conclusion = target.value;
            else if (target.dataset.inspectionDecision) draft.decision = target.value;
            else if (target.dataset.inspectionNote && item) item.note = target.value;
            else if (target.dataset.inspectionNotChecked && item) { item.answer = ""; item.status = 2; }
            else if (target.dataset.inspectionAnswer && item) {
                item.answer = target.dataset.inspectionValue ?? target.value;
                if (item.answer) item.status = 1;
            }
            localStorage.setItem(key, JSON.stringify(draft));
        };
        root._landErpInspectionUpdate = update;
        root.addEventListener("input", update);
        root.addEventListener("change", update);
        root.addEventListener("click", update);
    },
    remove: function (key) { localStorage.removeItem(key); }
};
