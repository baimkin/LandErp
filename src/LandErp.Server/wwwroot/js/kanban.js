(() => {
const boards = new WeakMap();
const viewKey = userId => `landerp.procurement.view.v1.${userId}`;
window.LandErpKanban = {
  focus: null,
  rememberFocus() { this.focus = document.activeElement; },
  restoreFocus() { const el = this.focus; if (el?.isConnected) el.focus({ preventScroll: true }); },
  loadView(userId) {
    try {
      const value = JSON.parse(localStorage.getItem(viewKey(userId)));
      if (!value || !["table", "kanban"].includes(value.view)) return null;
      if (value.pipelineId != null && !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value.pipelineId)) return null;
      const last = value.lastPipelineId ?? value.pipelineId;
      const validLast = typeof last === "string" && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(last);
      return { view: value.view, pipelineId: value.pipelineId ?? null, lastPipelineId: validLast ? last : null };
    } catch { return null; }
  },
  saveView(userId, value) {
    try {
      const previous = this.loadView(userId);
      localStorage.setItem(viewKey(userId), JSON.stringify({ view: value.view, pipelineId: value.pipelineId,
        lastPipelineId: value.pipelineId ?? value.lastPipelineId ?? previous?.lastPipelineId ?? null }));
    }
    catch { /* A blocked browser store must not prevent switching views. */ }
  },
  attach(board, callback, feedback) {
    this.detach(board);
    const controller = new AbortController();
    const options = { signal: controller.signal };
    let card = null, over = null, pending = false, suppressClickUntil = 0;
    const highlight = column => {
      if (over === column) return;
      over?.classList.remove("over");
      over = column;
      over?.classList.add("over");
    };
    const clear = () => {
      card?.classList.remove("dragging");
      card = null;
      highlight(null);
    };
    const columnAt = event => {
      const column = event.target.closest?.("[data-stage-id]");
      return column && board.contains(column) ? column : null;
    };
    board.addEventListener("dragstart", event => {
      const target = event.target.closest?.("[data-membership-id]");
      if (!target || !board.contains(target) || target.getAttribute("draggable") !== "true" || board.dataset.busy === "true" || pending || !event.dataTransfer) {
        event.preventDefault();
        return;
      }
      card = target;
      feedback.hidden = true;
      // Native drag events require synchronous DataTransfer/preventDefault, before any server round trip.
      event.dataTransfer.setData("text/plain", card.dataset.membershipId);
      event.dataTransfer.effectAllowed = "move";
      card.classList.add("dragging");
    }, options);
    board.addEventListener("dragover", event => {
      if (!card || pending || board.dataset.busy === "true") return;
      event.preventDefault();
      if (event.dataTransfer) event.dataTransfer.dropEffect = "move";
      highlight(columnAt(event));
      const bounds = board.getBoundingClientRect();
      const x = event.clientX < bounds.left + 36 ? -18 : event.clientX > bounds.right - 36 ? 18 : 0;
      const y = event.clientY < bounds.top + 36 ? -18 : event.clientY > bounds.bottom - 36 ? 18 : 0;
      if (x || y) board.scrollBy(x, y);
    }, options);
    board.addEventListener("dragleave", event => {
      if (!board.contains(event.relatedTarget)) highlight(null);
    }, options);
    board.addEventListener("drop", async event => {
      if (!card) return;
      event.preventDefault();
      const column = columnAt(event);
      const memberId = card.dataset.membershipId;
      const sourceStage = card.closest("[data-stage-id]")?.dataset.stageId;
      const stageId = column?.dataset.stageId;
      suppressClickUntil = performance.now() + 300;
      clear();
      if (!stageId || sourceStage === stageId || pending || board.dataset.busy === "true") return;
      pending = true;
      try { await callback.invokeMethodAsync("DropCard", memberId, stageId); }
      catch {
        if (controller.signal.aborted) return;
        feedback.textContent = "Не удалось подтвердить перенос. Обновите страницу и проверьте этап объекта.";
        feedback.hidden = false;
      }
      finally { pending = false; }
    }, options);
    board.addEventListener("dragend", () => { suppressClickUntil = performance.now() + 300; clear(); }, options);
    board.addEventListener("click", event => {
      if (performance.now() < suppressClickUntil) { event.preventDefault(); event.stopImmediatePropagation(); }
    }, { ...options, capture: true });
    boards.set(board, () => { controller.abort(); clear(); });
  },
  attachOrder(root, callback) {
    this.detach(root);
    const controller = new AbortController(), options = { signal: controller.signal };
    let source = null, pending = false;
    const header = event => event.target.closest?.("[data-order-stage]");
    root.addEventListener("dragstart", event => {
      const row = header(event);
      if (!row || !root.contains(row) || root.dataset.busy === "true" || pending || !event.dataTransfer) { event.preventDefault(); return; }
      source = row.dataset.orderStage;
      event.dataTransfer.setData("text/plain", source);
      event.dataTransfer.effectAllowed = "move";
    }, options);
    root.addEventListener("dragover", event => { if (source && header(event)) event.preventDefault(); }, options);
    root.addEventListener("dragend", () => { source = null; }, options);
    root.addEventListener("drop", async event => {
      const target = header(event)?.dataset.orderStage, from = source;
      if (!from) return;
      event.preventDefault(); source = null;
      if (!target || target === from || pending || root.dataset.busy === "true") return;
      pending = true;
      try { await callback.invokeMethodAsync("ReorderStage", from, target); }
      catch {
        const feedback = root.querySelector(".order-feedback");
        if (feedback && !controller.signal.aborted) {
          feedback.textContent = "Не удалось изменить порядок. Проверьте соединение; после восстановления можно использовать кнопки ↑ и ↓.";
          feedback.hidden = false;
        }
      }
      finally { pending = false; }
    }, options);
    boards.set(root, () => controller.abort());
  },
  detach(board) { boards.get(board)?.(); boards.delete(board); }
};
})();
