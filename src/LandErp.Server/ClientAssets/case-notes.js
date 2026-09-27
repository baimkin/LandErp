
const imageTypes = ['image/png','image/jpeg','image/webp','image/gif'];
export const isPlainDocument = doc => (doc.content || []).every(n =>
  n.type === 'image' || n.type === 'attachment' || n.type === 'paragraph' &&
  (n.content || []).every(c => c.type === 'hardBreak' || c.type === 'text' && !(c.marks?.length)));
export const plainParagraphs = text => text.split('\n').map(line =>
  line ? {type:'paragraph',content:[{type:'text',text:line}]} : {type:'paragraph'});

// Legacy formatted documents remain intact. New input appends plain paragraphs to them.
export function create(host, json, dotnet, label = 'Текст', maxLength = 64000) {
  const original = JSON.parse(json), plain = isPlainDocument(original);
  const preserved = plain ? [] : structuredClone(original.content || []);
  const textarea = document.createElement('textarea');
  textarea.setAttribute('aria-label',label); textarea.maxLength = maxLength;
  textarea.placeholder = plain ? 'Текст, изображения и файлы' : 'Дополнение к сохранённому тексту';
  textarea.value = plain ? (original.content || []).filter(n=>n.type==='paragraph')
    .map(n=>(n.content||[]).map(c=>c.type==='hardBreak'?'\n':c.text||'').join('')).join('\n') : '';
  const files = plain ? (original.content || []).filter(n=>n.type==='image'||n.type==='attachment')
    .map(n=>({node:n,name:n.attrs.name || 'Изображение', id:n.attrs.attachmentId})) : [];
  const list=document.createElement('div'); list.className='draft-files';
  const status=document.createElement('div'); status.className='draft-status'; status.setAttribute('role','status');
  const picker=document.createElement('input'); picker.type='file'; picker.multiple=true; picker.hidden=true;
  picker.accept='image/png,image/jpeg,image/webp,image/gif,audio/*,.pdf,.txt,.csv,.docx,.xlsx';
  const attach=document.createElement('button'); attach.type='button';attach.textContent='📎 Прикрепить фото или файл';attach.onclick=()=>picker.click();
  host.replaceChildren(textarea, list, attach, picker, status);
  let dirty=false, frozen=false, destroyed=false;
  const changed=()=>{dirty=true;void dotnet.invokeMethodAsync('OnChanged').catch(()=>{});};
  textarea.oninput=changed;
  function render(){
    list.replaceChildren();
    for (const file of files) {
      const row=document.createElement('div');row.className='draft-file';
      if(file.preview || file.node?.type==='image'){
        const img=document.createElement('img');img.alt='';img.src=file.preview || '/api/procurement/attachments/'+file.id+'/image';row.append(img);
      }
      const name=document.createElement('span');name.textContent=file.name;row.append(name);
      const remove=document.createElement('button');remove.type='button';remove.textContent='×';remove.setAttribute('aria-label','Убрать '+file.name+' из черновика');
      remove.disabled=frozen;remove.onclick=()=>{if(frozen)return;files.splice(files.indexOf(file),1);if(file.preview)URL.revokeObjectURL(file.preview);changed();render();};row.append(remove);list.append(row);
    }
  }
  function add(incoming) {
    if(frozen)return;
    for(const file of incoming){
      if(!file.size || file.size>8*1024*1024 || files.filter(f=>f.file).length>=6){status.textContent='До 6 новых файлов по 8 МБ.';continue;}
      files.push({file,name:file.name,preview:imageTypes.includes(file.type)?URL.createObjectURL(file):null});changed();
    }
    render();
  }
  picker.onchange=()=>{add([...picker.files]);picker.value='';};
  textarea.onpaste=e=>{const images=[...(e.clipboardData?.files||[])].filter(f=>imageTypes.includes(f.type));if(images.length){e.preventDefault();add(images);}};
  function freeze(value){frozen=value;textarea.disabled=value;attach.disabled=value;render();}
  async function collectFiles(){
    freeze(true);
    try {
      for(const file of files){
        if(file.id || !file.file)continue;
        status.textContent='Подготовка: '+file.name;
        const id=await dotnet.invokeMethodAsync('UploadFile',DotNet.createJSStreamReference(new Uint8Array(await file.file.arrayBuffer())),file.name,file.file.type || 'application/octet-stream');
        if(!id)throw Error('Не удалось загрузить файл. Черновик сохранён.');
        file.id=id;
        file.node={type:imageTypes.includes(file.file.type)?'image':'attachment',attrs:{attachmentId:id,name:file.name}};
      }
      status.textContent='';
      return files.map(f=>f.id);
    } catch(error) { status.textContent=error.message;freeze(false);throw error; }
  }
  const beforeUnload=e=>{if(dirty||frozen){e.preventDefault();e.returnValue='';}};
  window.addEventListener('beforeunload',beforeUnload);
  render();
  return DotNet.createJSObjectReference({
    text(){return textarea.value;},
    collectFiles,
    async document(){
      await collectFiles();
      const content=[...preserved,...plainParagraphs(textarea.value),...files.map(f=>f.node)];
      const blob=new Blob([JSON.stringify({type:'doc',content})],{type:'application/json'});
      if(blob.size>128*1024){freeze(false);throw Error('Текст превышает 128 КБ.');}
      return DotNet.createJSStreamReference(blob);
    },
    markDirty(){dirty=true;},
    resume(){if(!destroyed)freeze(false);},
    confirmDiscard(){return !frozen && (!dirty||window.confirm('Отбросить несохранённые изменения?'));},
    destroy(){destroyed=true;window.removeEventListener('beforeunload',beforeUnload);for(const f of files)if(f.preview)URL.revokeObjectURL(f.preview);host.replaceChildren();}
  });
}
