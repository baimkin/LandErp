// The list stays mounted. Restore both the page/ancestor scroll and horizontal table position.
function ancestors(root) {
    const elements = [];
    for (let element = root; element; element = element.parentElement) elements.push(element);
    return elements;
}
export function capture(root) {
    const table = root?.querySelector('.v2-table-wrap');
    return {
        ancestors: ancestors(root).map(element => ({ top: element.scrollTop, left: element.scrollLeft })),
        table: table ? { top: table.scrollTop, left: table.scrollLeft } : null,
        x: window.scrollX, y: window.scrollY
    };
}
export async function restore(root, position) {
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    if (!root?.isConnected) return; // A navigation must not restore the previous screen's position.
    ancestors(root).forEach((element, index) => {
        const saved = position.ancestors[index];
        if (saved) { element.scrollTop = saved.top; element.scrollLeft = saved.left; }
    });
    const table = root?.querySelector('.v2-table-wrap');
    if (table && position.table) { table.scrollTop = position.table.top; table.scrollLeft = position.table.left; }
    window.scrollTo({ left: position.x, top: position.y, behavior: 'instant' });
}
