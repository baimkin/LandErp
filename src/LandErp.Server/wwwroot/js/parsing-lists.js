// List state belongs to one browser tab and one organization/employee, never to shared server state.
const sessions = new WeakMap();
const tabs = ['home','zones','searches','history'];
const strings = ['tab','zoneQuery','zoneState','query','group','source','status','groupVisibility','historyQuery','historySource','historyResult'];
const safeRead = key => {
    try {
        const text = sessionStorage.getItem(key);
        const data = text && text.length < 40000 ? JSON.parse(text) : {};
        if (!data || typeof data !== 'object' || Array.isArray(data)) return {};
        const preferences = {};
        for(const name of strings) if(typeof data.preferences?.[name] === 'string') preferences[name] = data.preferences[name].slice(0,200);
        if(Number.isFinite(data.preferences?.historyCount)) preferences.historyCount = Math.max(50,Math.min(10000,Math.floor(data.preferences.historyCount)));
        const positions = {};
        for(const key of [...tabs,...tabs.map(t=>'page-'+t)]) {
            const p = data.positions?.[key];
            if(p && typeof p === 'object') positions[key] = {top:bounded(p.top),left:bounded(p.left),anchor:typeof p.anchor==='string'?p.anchor.slice(0,128):null,offset:Number.isFinite(p.offset)?p.offset:0};
        }
        return {preferences,positions};
    } catch { return {}; }
};
const save = s => { try { sessionStorage.setItem(s.key, JSON.stringify({ preferences:s.preferences, positions:s.positions })); } catch { /* Storage is optional. */ } };
const bounded = n => Number.isFinite(n) ? Math.max(0, Math.min(n, 1e8)) : 0;
export function loadPreferences(key) { return safeRead(key)?.preferences ?? null; }

function capture(s, element) {
    if (!element.getClientRects().length) return;
    const top = element.getBoundingClientRect().top;
    const head = element.querySelector('thead')?.getBoundingClientRect().height ?? 0;
    const row = [...element.querySelectorAll('tbody tr[data-row-key]')].find(r => r.getBoundingClientRect().bottom > top + head + 1);
    s.positions[element.dataset.scrollList] = {
        top:element.scrollTop, left:element.scrollLeft,
        anchor:row?.dataset.rowKey ?? null, offset:row ? row.getBoundingClientRect().top - top : 0
    };
    save(s);
}
function restore(s) {
    s.restoring = true;
    for (const element of s.root.querySelectorAll('[data-scroll-list]')) {
        if (!element.getClientRects().length) continue;
        const p = s.positions[element.dataset.scrollList];
        if (!p) continue;
        let top = bounded(p.top);
        if (p.anchor && matchMedia('(min-width:821px)').matches) {
            // IDs are matched as data, not interpolated into selectors from storage.
            const row = [...element.querySelectorAll('tbody tr[data-row-key]')].find(r => r.dataset.rowKey === p.anchor);
            if (row) top = element.scrollTop + row.getBoundingClientRect().top - element.getBoundingClientRect().top - (Number.isFinite(p.offset) ? p.offset : 0);
        }
        element.scrollTop = Math.max(0, top);
        element.scrollLeft = bounded(p.left);
    }
    if(s.restorePage || matchMedia('(max-width:820px)').matches) {
        const p = s.positions['page-'+s.active];
        let top = bounded(p?.top);
        if(p?.anchor && matchMedia('(max-width:820px)').matches) {
            const panel = [...s.root.querySelectorAll('[data-scroll-list]')].find(e=>e.dataset.scrollList===s.active);
            const row = panel && [...panel.querySelectorAll('tbody tr[data-row-key]')].find(r=>r.dataset.rowKey===p.anchor);
            if(row) top = scrollY + row.getBoundingClientRect().top - p.offset;
        }
        window.scrollTo({top:Math.max(0,top),left:0,behavior:'instant'});
        s.restorePage = false;
    }
    s.restoring = false;
}
function layout(s) {
    if (!matchMedia('(min-width:821px)').matches) return;
    for (const element of s.root.querySelectorAll('[data-scroll-list]')) {
        if (!element.getClientRects().length) continue;
        const panel = element.closest('.list-panel');
        const footer = panel?.querySelector('.list-footer')?.getBoundingClientRect().height ?? 55;
        const available = Math.max(220, innerHeight - element.getBoundingClientRect().top - footer - 20);
        const value = `${Math.floor(available)}px`;
        if (element.style.getPropertyValue('--list-body-height') !== value) element.style.setProperty('--list-body-height', value);
    }
}
function schedule(s) {
    if (s.frame) cancelAnimationFrame(s.frame);
    s.frame = requestAnimationFrame(() => {
        s.frame = 0;
        layout(s);
        restore(s);
    });
}
export function mount(root, key) {
    if (sessions.has(root)) return;
    const saved = safeRead(key);
    const s = { root,key,preferences:saved?.preferences ?? {},positions:saved?.positions ?? {},frame:0,restoring:false,active:root.dataset.listsTab,restorePage:true };
    const scroll = event => {
        if (s.restoring) return;
        const element = event.target;
        if (element instanceof HTMLElement && element.matches('[data-scroll-list]')) capture(s, element);
    };
    s.scroll = scroll;
    s.pageScroll = () => {
        if(s.restoring || s.restorePage || root.dataset.listsTab!==s.active) return;
        const panel = [...root.querySelectorAll('[data-scroll-list]')].find(e=>e.dataset.scrollList===s.active);
        const row = panel && [...panel.querySelectorAll('tbody tr[data-row-key]')].find(r=>r.getBoundingClientRect().bottom>130);
        s.positions['page-'+s.active] = {top:scrollY,left:0,anchor:row?.dataset.rowKey??null,offset:row?.getBoundingClientRect().top??0};
        save(s);
    };
    s.resize = () => schedule(s);
    root.addEventListener('scroll', scroll, true);
    window.addEventListener('resize', s.resize);
    window.addEventListener('scroll',s.pageScroll,{passive:true});
    s.observer = new MutationObserver(() => schedule(s));
    s.observer.observe(root, {childList:true,subtree:true,characterData:true});
    s.resizeObserver = new ResizeObserver(() => schedule(s));
    for(const header of root.querySelectorAll('.list-panel .filters,.list-panel .section-heading')) s.resizeObserver.observe(header);
    sessions.set(root,s);
    schedule(s);
}
export function update(root, preferences, resets = []) {
    const s = sessions.get(root);
    if (!s) return;
    s.preferences = preferences;
    if(s.active!==root.dataset.listsTab) { s.active = root.dataset.listsTab; s.restorePage = true; }
    for(const key of resets) {
        s.positions[key] = {top:0,left:0,anchor:null,offset:0};
        s.positions['page-'+key] = {top:0,left:0,anchor:null,offset:0};
        if(key===s.active) s.restorePage = true;
    }
    save(s);
    schedule(s);
}
export function dispose(root) {
    const s = sessions.get(root);
    if(!s) return;
    save(s);
    if(s.frame) cancelAnimationFrame(s.frame);
    s.observer.disconnect(); s.resizeObserver.disconnect();
    root.removeEventListener('scroll',s.scroll,true); window.removeEventListener('resize',s.resize);
    window.removeEventListener('scroll',s.pageScroll);
    sessions.delete(root);
}
