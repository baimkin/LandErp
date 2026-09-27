// No browser or application host: exercise the input adapter against a minimal DOM boundary.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../src/LandErp.Server/ClientAssets/case-notes.js', import.meta.url), 'utf8');
const { create, isPlainDocument } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
class Element {
  constructor(tag) { this.tag = tag; this.children = []; this.value = ''; }
  setAttribute(name, value) { this[name] = value; }
  append(...items) { this.children.push(...items); }
  replaceChildren(...items) { this.children = items; }
  click() { this.onclick?.(); }
}
globalThis.document = { createElement: tag => new Element(tag) };
globalThis.window = { addEventListener() {}, removeEventListener() {}, confirm: () => true };
globalThis.DotNet = { createJSObjectReference: value => value, createJSStreamReference: value => value };
const empty = '{"type":"doc","content":[{"type":"paragraph"}]}';
let uploads = 0, fail = false;
const bridge = { async invokeMethodAsync(method) {
  if (method === 'OnChanged') return;
  uploads++;
  if (fail) return null;
  return '00000000-0000-0000-0000-' + String(uploads).padStart(12, '0');
} };
const file = new File(['data'], 'note.txt', {type:'text/plain'});
const image = new File(['image'], 'paste.png', {type:'image/png'});
const host = new Element('div');
const input = create(host, empty, bridge);
const [textarea, list, , picker] = host.children;
textarea.value = '<literal>\nВторая строка'; textarea.oninput();
picker.files = [file]; picker.onchange();
assert.equal(uploads, 0, 'Choosing a file must not publish it');
assert.equal(list.children.length, 1);
list.children[0].children.at(-1).click();
assert.equal(list.children.length, 0, 'Remove only from draft');
assert.equal(uploads, 0);
let prevented = false;
textarea.onpaste({clipboardData:{files:[image]},preventDefault(){prevented=true;}});
assert.equal(prevented, true);
assert.equal(uploads, 0, 'Clipboard image stays local until save');
textarea.onpaste({clipboardData:{files:[]},preventDefault(){throw Error('Plain paste must stay native');}});
fail = true;
await assert.rejects(input.document());
assert.equal(input.text(), '<literal>\nВторая строка');
fail = false;
let doc = JSON.parse(await (await input.document()).text());
assert.equal(doc.content[0].content[0].text, '<literal>');
assert.equal(doc.content[2].type, 'image');
const uploaded = uploads;
input.resume();
await input.document();
assert.equal(uploads, uploaded, 'Retry must reuse uploaded attachment ID');
input.resume(); input.destroy();

const legacy = {type:'doc',content:[{type:'table',content:[{type:'tableRow',content:[{type:'tableCell',content:[{type:'paragraph',content:[{type:'text',text:'Старая таблица',marks:[{type:'bold'}]}]}]}]}]}]};
assert.equal(isPlainDocument(legacy), false);
const legacyHost = new Element('div'), legacyInput = create(legacyHost, JSON.stringify(legacy), bridge);
legacyHost.children[0].value = 'Дополнение';
doc = JSON.parse(await (await legacyInput.document()).text());
assert.deepEqual(doc.content[0], legacy.content[0], 'Legacy nodes remain byte-equivalent as data');
assert.equal(doc.content[1].content[0].text, 'Дополнение');
legacyInput.resume(); legacyInput.destroy();

const cancelledHost = new Element('div'), cancelled = create(cancelledHost, empty, bridge);
cancelledHost.children[3].files=[file];cancelledHost.children[3].onchange();
assert.equal(cancelled.confirmDiscard(), true); cancelled.destroy();
assert.equal(uploads, uploaded, 'Cancel never uploads a file');
console.log('PASS: plain text, native paste, image paste, file removal, deferred upload, failure/retry, legacy table, cancel');
