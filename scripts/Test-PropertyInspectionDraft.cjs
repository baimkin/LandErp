const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const source=fs.readFileSync(require('node:path').join(__dirname,'../src/LandErp.Server/Components/Pages/SiteInspectionPage.razor.js'),'utf8').replaceAll('export function','function');
function setup(values=new Map()) {
 const handlers={},root={dataset:{}};
 const storage={getItem:k=>values.get(k)??null,setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k),key:i=>[...values.keys()][i]??null,get length(){return values.size;}};
 const context=vm.createContext({localStorage:storage,document:{querySelector:()=>root,addEventListener:(n,fn)=>handlers[n]=fn},structuredClone});
 vm.runInContext(source,context);return {context,values,handlers,root,storage};
}
const draft=version=>({inspectionId:'inspection-1',version,answers:[{id:'item-1',answer:'',status:0}],conclusion:'',decision:''});
function answer(handlers,value){const target={dataset:{inspectionAnswer:'item-1',inspectionValue:value},closest(selector){return selector==='fieldset:disabled'?null:this;}};handlers.click({target});}
test('offline answer uses latest server version after reattach',()=>{const s=setup();s.context.attach('key',draft(1));answer(s.handlers,'old');s.context.remove('key');s.context.attach('key',draft(2));answer(s.handlers,'new');const saved=s.context.load('key');assert.equal(saved.version,2);assert.equal(saved.answers[0].answer,'new');});
test('same-version local answers survive another input',()=>{const s=setup();s.context.attach('key',draft(1));answer(s.handlers,'yes');const target={dataset:{inspectionNote:'item-1'},value:'note',closest(selector){return selector==='fieldset:disabled'?null:this;}};s.handlers.input({target});const saved=s.context.load('key');assert.equal(saved.answers[0].answer,'yes');assert.equal(saved.answers[0].note,'note');});
test('storage unavailable does not throw from DOM listener',()=>{const s=setup();s.storage.setItem=()=>{throw Error('quota');};s.context.attach('key',draft(1));assert.doesNotThrow(()=>answer(s.handlers,'yes'));});
test('unrelated click does not recreate removed draft',()=>{const s=setup();s.context.attach('key',draft(1));s.context.remove('key');s.handlers.click({target:{closest:()=>null}});assert.equal(s.context.load('key'),null);});

test('tabs retain separate branches and exact acknowledgement keeps newer edits',()=>{
 const a=setup(),b=setup(a.values);a.context.attach('key',draft(1));b.context.attach('key',draft(1));
 answer(a.handlers,'A');const rev=a.context.load('key').revision;
 answer(b.handlers,'B');assert.equal(a.context.loadAll('key').length,2);
 answer(a.handlers,'new A');a.context.acknowledge('key',[rev]);
 assert.equal(a.context.loadAll('key').length,2);
 const current=a.context.load('key').revision;a.context.acknowledge('key',[current]);
 assert.equal(b.context.loadAll('key').length,1);assert.equal(b.context.load('key').answers[0].answer,'B');
});
test('new document recovers unsaved branch, including modal conclusion',()=>{
 const a=setup();a.context.attach('key',draft(1));
 a.handlers.input({target:{dataset:{inspectionConclusion:'true'},value:'offline conclusion',closest(selector){return selector==='fieldset:disabled'?null:this;}}});
 const b=setup(a.values);assert.equal(b.context.loadAll('key')[0].conclusion,'offline conclusion');
});
test('legacy draft is retained until exact content acknowledgement',()=>{
 const s=setup();s.storage.setItem('old',JSON.stringify(draft(1)));
 const rev=s.context.loadAll('key','old')[0].revision;
 s.storage.setItem('old',JSON.stringify({...draft(1),conclusion:'new'}));
 s.context.acknowledge('key',[rev],'old');assert.ok(s.storage.getItem('old'));
});
test('item version changes invalidate stale DOM snapshot even with unchanged inspection version',()=>{
 const s=setup(),initial=draft(1);initial.answers[0].version=1;s.context.attach('key',initial);answer(s.handlers,'old');
 const current=draft(1);current.answers[0].version=2;current.answers[0].note='saved on server';s.context.attach('key',current);
 answer(s.handlers,'new');const saved=s.context.load('key');assert.equal(saved.answers[0].version,2);assert.equal(saved.answers[0].note,'saved on server');
});
