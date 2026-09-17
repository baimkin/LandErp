const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const source=fs.readFileSync(require('node:path').join(__dirname,'../src/LandErp.Server/Components/Pages/SiteInspectionPage.razor.js'),'utf8').replaceAll('export function','function');
function setup(){const values=new Map(),handlers={};const root={dataset:{},addEventListener:(name,fn)=>handlers[name]=fn};const storage={getItem:k=>values.get(k)??null,setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)};const context=vm.createContext({localStorage:storage,document:{querySelector:()=>root},structuredClone});vm.runInContext(source,context);return {context,values,handlers,root,storage};}
const draft=version=>({inspectionId:'inspection-1',version,answers:[{id:'item-1',answer:'',status:0}],conclusion:'',decision:''});
function answer(handlers,value){const target={dataset:{inspectionAnswer:'item-1',inspectionValue:value},closest(){return this;}};handlers.click({target});}
test('offline answer uses latest server version after reattach',()=>{const s=setup();s.context.attach('key',draft(1));answer(s.handlers,'old');s.context.remove('key');s.context.attach('key',draft(2));answer(s.handlers,'new');const saved=s.context.load('key');assert.equal(saved.version,2);assert.equal(saved.answers[0].answer,'new');});
test('same-version local answers survive another input',()=>{const s=setup();s.context.attach('key',draft(1));answer(s.handlers,'yes');const target={dataset:{inspectionNote:'item-1'},value:'note',closest(){return this;}};s.handlers.input({target});const saved=s.context.load('key');assert.equal(saved.answers[0].answer,'yes');assert.equal(saved.answers[0].note,'note');});
test('storage unavailable does not throw from DOM listener',()=>{const s=setup();s.storage.setItem=()=>{throw Error('quota');};s.context.attach('key',draft(1));assert.doesNotThrow(()=>answer(s.handlers,'yes'));});
test('unrelated click does not recreate removed draft',()=>{const s=setup();s.context.attach('key',draft(1));s.context.remove('key');s.handlers.click({target:{closest:()=>null}});assert.equal(s.context.load('key'),null);});
