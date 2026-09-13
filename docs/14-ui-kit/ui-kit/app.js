const pageNames = {
  overview: "Основы и обзор",
  components: "Компоненты",
  states: "Состояния",
  queue: "Очередь менеджера",
  property: "Карточка участка",
  project: "Проект и финансы",
  agents: "Мониторинг Agent",
  investor: "Кабинет инвестора"
};

const sidebar = document.querySelector("#sidebar");
const pageTitle = document.querySelector("#pageTitle");

document.querySelectorAll("[data-page]").forEach((button) => {
  button.addEventListener("click", () => {
    const page = button.dataset.page;
    document.querySelectorAll("[data-page]").forEach((item) => item.classList.toggle("active", item === button));
    document.querySelectorAll("[data-panel]").forEach((panel) => panel.classList.toggle("active", panel.dataset.panel === page));
    pageTitle.textContent = pageNames[page];
    sidebar.classList.remove("open");
    window.scrollTo({ top: 0, behavior: "smooth" });
  });
});

document.querySelector("#menuToggle").addEventListener("click", () => sidebar.classList.toggle("open"));

document.querySelectorAll("[data-density-value]").forEach((button) => {
  button.addEventListener("click", () => {
    document.body.dataset.density = button.dataset.densityValue;
    document.querySelectorAll("[data-density-value]").forEach((item) => item.classList.toggle("active", item === button));
  });
});

const toast = document.querySelector("#toast");
let toastTimer;
function showToast(message) {
  toast.querySelector("span").textContent = message;
  toast.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.remove("show"), 2600);
}
document.querySelectorAll("[data-toast]").forEach((button) => button.addEventListener("click", () => showToast(button.dataset.toast)));

const dialog = document.querySelector("#demoDialog");
document.querySelector("#openDialog").addEventListener("click", () => dialog.showModal());

const drawer = document.querySelector("#demoDrawer");
const drawerBackdrop = document.querySelector("#drawerBackdrop");
function setDrawer(open) {
  drawer.classList.toggle("open", open);
  drawerBackdrop.classList.toggle("open", open);
  drawer.setAttribute("aria-hidden", String(!open));
  drawer.inert = !open;
  if (open) drawer.querySelector("button").focus();
}
document.querySelector("#openDrawer").addEventListener("click", () => setDrawer(true));
document.querySelector("#closeDrawer").addEventListener("click", () => setDrawer(false));
document.querySelector("#closeDrawerBottom").addEventListener("click", () => setDrawer(false));
drawerBackdrop.addEventListener("click", () => setDrawer(false));
document.addEventListener("keydown", (event) => { if (event.key === "Escape") setDrawer(false); });

document.querySelectorAll("[data-detail-tab]").forEach((button) => {
  button.addEventListener("click", () => {
    const target = button.dataset.detailTab;
    document.querySelectorAll("[data-detail-tab]").forEach((item) => item.classList.toggle("active", item === button));
    document.querySelectorAll("[data-detail-panel]").forEach((panel) => panel.classList.toggle("active", panel.dataset.detailPanel === target));
  });
});

const selectAll = document.querySelector("#selectAll");
const rowChecks = [...document.querySelectorAll(".row-check")];
const selectedCount = document.querySelector("#selectedCount");
function updateSelection() {
  const count = rowChecks.filter((check) => check.checked).length;
  selectedCount.textContent = count ? `Выбрано: ${count}` : "Ничего не выбрано";
  selectAll.checked = count === rowChecks.length;
  selectAll.indeterminate = count > 0 && count < rowChecks.length;
}
selectAll.addEventListener("change", () => { rowChecks.forEach((check) => { check.checked = selectAll.checked; }); updateSelection(); });
rowChecks.forEach((check) => check.addEventListener("change", updateSelection));

document.querySelector("#demoForm").addEventListener("submit", (event) => {
  event.preventDefault();
  showToast("В демонстрации форма не изменяет данные");
});
