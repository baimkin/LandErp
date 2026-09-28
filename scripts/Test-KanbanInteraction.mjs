// Logic-only checks; no browser, rendering, or visual acceptance.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const storage = new Map();
const context = { window: {}, AbortController, performance, localStorage: {
  getItem: key => storage.get(key) ?? null,
  setItem: (key, value) => storage.set(key, value)
} };
vm.runInNewContext(readFileSync(new URL('../src/LandErp.Server/wwwroot/js/kanban.js', import.meta.url), 'utf8'), context);
const api = context.window.LandErpKanban;
const pipelineId = '11111111-1111-1111-1111-111111111111';
api.saveView('alice', { view: 'kanban', pipelineId });
api.saveView('bob', { view: 'table', pipelineId: null });
assert.equal(api.loadView('alice').view, 'kanban');
assert.equal(api.loadView('alice').pipelineId, pipelineId);
assert.equal(api.loadView('bob').view, 'table');
api.saveView('alice', { view: 'table', pipelineId: null });
assert.equal(api.loadView('alice').pipelineId, null);
assert.equal(api.loadView('alice').lastPipelineId, pipelineId);
assert.equal(api.loadView('bob').lastPipelineId, null);
assert.equal(api.loadView('new-user'), null);
storage.set('landerp.procurement.view.v1.alice', '{broken');
assert.equal(api.loadView('alice'), null);
api.saveView('alice', { view: 'other', pipelineId });
assert.equal(api.loadView('alice'), null);
api.saveView('alice', { view: 'kanban', pipelineId: 'bad-id' });
assert.equal(api.loadView('alice'), null);
context.localStorage.getItem = () => { throw new Error('Storage blocked'); };
context.localStorage.setItem = () => { throw new Error('Storage blocked'); };
assert.equal(api.loadView('alice'), null);
assert.doesNotThrow(() => api.saveView('alice', { view: 'table' }));

const classes = () => {
  const set = new Set();
  return { add: value => set.add(value), remove: value => set.delete(value), contains: value => set.has(value) };
};
const source = { dataset: { stageId: 'source' }, classList: classes() };
const target = { dataset: { stageId: 'target' }, classList: classes() };
target.closest = () => target;
const card = { dataset: { membershipId: 'member' }, classList: classes(),
  getAttribute: () => 'true', closest: selector => selector === '[data-membership-id]' ? card : source };
const handlers = new Map();
const board = { dataset: { busy: 'false' }, contains: item => [card, source, target].includes(item),
  addEventListener(name, fn, options) { handlers.set(name, fn); options.signal.addEventListener('abort', () => handlers.delete(name)); },
  getBoundingClientRect: () => ({ left: 0, right: 1000, top: 0, bottom: 800 }), scrollBy() {} };
const event = item => ({ target: item, relatedTarget: null, clientX: 500, clientY: 400, prevented: false,
  preventDefault() { this.prevented = true; }, dataTransfer: { setData(type, id) { this.type = type; this.id = id; } } });
const feedback = { hidden: true };
const moves = [];
let complete;
api.attach(board, { invokeMethodAsync(...args) { moves.push(args); return new Promise(resolve => { complete = resolve; }); } }, feedback);
const start = event(card);
handlers.get('dragstart')(start);
assert.equal(start.dataTransfer.id, 'member');
assert.equal(start.dataTransfer.effectAllowed, 'move');
const over = event(target);
handlers.get('dragover')(over);
assert.equal(over.prevented, true);
assert.equal(target.classList.contains('over'), true);
const drop = handlers.get('drop')(event(target));
assert.deepEqual(moves, [['DropCard', 'member', 'target']]);
assert.equal(target.classList.contains('over'), false);
const pendingStart = event(card);
handlers.get('dragstart')(pendingStart);
assert.equal(pendingStart.prevented, true);
await handlers.get('drop')(event(target));
assert.equal(moves.length, 1);
complete();
await drop;
board.dataset.busy = 'true';
const busyStart = event(card);
handlers.get('dragstart')(busyStart);
assert.equal(busyStart.prevented, true);
api.detach(board);
assert.equal(handlers.size, 0);
board.dataset.busy = 'false';
card.dataset.orderStage = 'first';
target.dataset.orderStage = 'second';
card.closest = () => card;
const orders = [];
api.attachOrder(board, { async invokeMethodAsync(...args) { orders.push(args); } });
const orderStart = event(card);
handlers.get('dragstart')(orderStart);
assert.equal(orderStart.dataTransfer.id, 'first');
await handlers.get('drop')(event(target));
assert.deepEqual(orders, [['ReorderStage', 'first', 'second']]);
api.detach(board);
assert.equal(handlers.size, 0);
console.log('Passed: account isolation, preference validation/storage failure, synchronous native drag setup, single pending drop, busy guard, detach. No browser verification.');
