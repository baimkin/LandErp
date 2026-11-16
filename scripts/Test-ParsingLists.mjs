import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const code = await readFile(new URL('../src/LandErp.Server/wwwroot/js/parsing-lists.js',import.meta.url),'utf8');
const {loadPreferences} = await import('data:text/javascript;base64,'+Buffer.from(code).toString('base64'));
let value;
globalThis.sessionStorage = {getItem:()=>value};
for(const bad of [undefined,'null','[]','42','{','"text"']) {
    value = bad;
    assert.deepEqual(loadPreferences('test') ?? {},{});
}
value = JSON.stringify({preferences:{tab:'history',historyCount:150,query:'Зона',source:'Cian'}});
assert.deepEqual(loadPreferences('test'),{tab:'history',query:'Зона',source:'Cian',historyCount:150});
value = JSON.stringify({preferences:{tab:42,historyCount:1e20,query:'x'.repeat(500),unknown:'ignored'}});
assert.deepEqual(loadPreferences('test'),{query:'x'.repeat(200),historyCount:10000});
value = JSON.stringify({preferences:{historyCount:-50}});
assert.equal(loadPreferences('test').historyCount,50);
value = ' '.repeat(40001);
assert.deepEqual(loadPreferences('test'),{});
globalThis.sessionStorage = {getItem:()=>{throw new Error('Storage denied');}};
assert.equal(loadPreferences('test'),null);
console.log('Passed: 11 storage validation cases; real scroll/anchors are covered by the browser test.');
