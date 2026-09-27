import { Editor, mergeAttributes } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Image from '@tiptap/extension-image';
import { TableKit } from '@tiptap/extension-table';

const imageUrl = id => `/api/procurement/attachments/${id}/image`;
const validId = id => typeof id === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id);
const CaseImage = Image.extend({
  addAttributes() { return { attachmentId:{default:null} }; },
  // Pasted HTML images never load external URLs; images enter only after protected upload.
  parseHTML() { return []; },
  renderHTML({node}) {
    const id=node.attrs.attachmentId;
    return ['img', mergeAttributes({src:validId(id)?imageUrl(id):'', alt:'Изображение заметки'})];
  }
});

export function create(host, json, dotnet) {
  const toolbar=document.createElement('div'); toolbar.className='note-toolbar'; toolbar.setAttribute('role','toolbar'); toolbar.setAttribute('aria-label','Форматирование текста');
  const content=document.createElement('div');
  const status=document.createElement('div'); status.className='note-upload-status'; status.setAttribute('role','status');
  host.replaceChildren(toolbar,content,status);
  let dirty=false, pending=0, destroyed=false, frozen=false;
  const root=host.closest('.rich-note');
  const markDirty=()=>{if(!dirty){dirty=true;void dotnet.invokeMethodAsync('Changed').catch(()=>{});}};
  // Check title/state fields share the synchronous browser-side leave guard with the document.
  const fieldChanged=event=>{if(event.target?.type!=='file')markDirty();};
  root?.addEventListener('input',fieldChanged);
  root?.addEventListener('change',fieldChanged);
  const buttons=[];
  const beforeUnload=e=>{if(dirty||pending){e.preventDefault();e.returnValue='';}};
  window.addEventListener('beforeunload',beforeUnload);
  const editor=new Editor({
    element:content, content:JSON.parse(json),
    extensions:[StarterKit.configure({
      blockquote:false,code:false,codeBlock:false,horizontalRule:false,strike:false,underline:false,trailingNode:false,
      heading:{levels:[1,2,3]},link:{openOnClick:false,autolink:false,linkOnPaste:false,protocols:['http','https']}
    }),CaseImage,TableKit.configure({table:{resizable:false}})],
    editorProps:{attributes:{'aria-label':'Текст секции','role':'textbox','aria-multiline':'true'},
      handlePaste(_view,event){
        const files=[...(event.clipboardData?.files||[])];
        if(files.length){event.preventDefault();void upload(files);return true;}return false;
      },
      handleDrop(_view,event){
        if(event.dataTransfer?.files.length){event.preventDefault();void upload([...event.dataTransfer.files]);return true;}return false;
      }
    },
    onUpdate(){ markDirty(); updateToolbar(); },
    onSelectionUpdate(){updateToolbar();}
  });
  function updateToolbar(){
    for(const {button,active} of buttons) if(active)button.setAttribute('aria-pressed',String(active()));
    styles.value=editor.isActive('heading')?String(editor.getAttributes('heading').level):'0';
  }
  function button(label,title,action,active){
    const b=document.createElement('button');b.type='button';b.textContent=label;b.title=title;b.setAttribute('aria-label',title);
    b.onmousedown=e=>e.preventDefault();b.onclick=()=>{if(!frozen){action();updateToolbar();}};
    toolbar.append(b);buttons.push({button:b,active});return b;
  }
  const styles=document.createElement('select');styles.setAttribute('aria-label','Стиль абзаца');
  for(const [value,label] of [['0','Текст'],['1','Заголовок 1'],['2','Заголовок 2'],['3','Заголовок 3']]){
    const option=document.createElement('option');option.value=value;option.textContent=label;styles.append(option);
  }
  styles.onchange=()=>{if(styles.value==='0')editor.chain().focus().setParagraph().run();else editor.chain().focus().setHeading({level:Number(styles.value)}).run();};toolbar.append(styles);
  button('Ж','Жирный (Ctrl+B)',()=>editor.chain().focus().toggleBold().run(),()=>editor.isActive('bold'));
  button('К','Курсив (Ctrl+I)',()=>editor.chain().focus().toggleItalic().run(),()=>editor.isActive('italic'));
  button('• Список','Маркированный список',()=>editor.chain().focus().toggleBulletList().run(),()=>editor.isActive('bulletList'));
  button('1. Список','Нумерованный список',()=>editor.chain().focus().toggleOrderedList().run(),()=>editor.isActive('orderedList'));
  button('Ссылка','Добавить или изменить ссылку',()=>{
    const href=window.prompt('Ссылка http:// или https:// (пусто — убрать)',editor.getAttributes('link').href||'');
    if(href===null)return;if(!href){editor.chain().focus().extendMarkRange('link').unsetLink().run();return;}
    try{const u=new URL(href);if(!['http:','https:'].includes(u.protocol)||u.username||u.password)throw Error();
      editor.chain().focus().extendMarkRange('link').setLink({href:u.href}).run();status.textContent='';
    }catch{status.textContent='Введите обычную http/https-ссылку без пароля.';}
  });
  button('Таблица','Вставить простую таблицу 3 × 3',()=>editor.chain().focus().insertTable({rows:3,cols:3,withHeaderRow:true}).run());
  button('+ Строка','Добавить строку ниже',()=>editor.chain().focus().addRowAfter().run());
  button('− Строка','Удалить строку',()=>editor.chain().focus().deleteRow().run());
  button('+ Столбец','Добавить столбец справа',()=>editor.chain().focus().addColumnAfter().run());
  button('− Столбец','Удалить столбец',()=>editor.chain().focus().deleteColumn().run());
  button('× Таблица','Удалить таблицу',()=>editor.chain().focus().deleteTable().run());
  button('↶','Отменить ввод (Ctrl+Z)',()=>editor.chain().focus().undo().run());
  button('↷','Повторить ввод',()=>editor.chain().focus().redo().run());
  const file=document.createElement('input');file.type='file';file.accept='image/png,image/jpeg,image/webp,image/gif';file.hidden=true;
  file.onchange=()=>{void upload([...file.files]);file.value='';};host.append(file);
  button('Изображение','Загрузить изображение (также Ctrl+V)',()=>file.click());
  async function upload(files){
    if(pending||frozen)return;
    pending++;
    try{
      for(const f of files){
        if(!['image/png','image/jpeg','image/webp','image/gif'].includes(f.type)||!f.size||f.size>8*1024*1024){status.textContent='PNG, JPEG, WebP или GIF, до 8 МБ.';continue;}
        status.textContent='Загрузка изображения…';
        const bytes=new Uint8Array(await f.arrayBuffer());
        const id=await dotnet.invokeMethodAsync('UploadImage',DotNet.createJSStreamReference(bytes),f.name,f.type);
        if(id&&!destroyed){editor.chain().focus().insertContent({type:'image',attrs:{attachmentId:id}}).run();status.textContent='Изображение загружено; сохраните текст.';}
        else status.textContent='Изображение не вставлено. Текущий текст сохранён в редакторе.';
      }
    }catch{status.textContent='Не удалось загрузить изображение. Текущий текст сохранён в редакторе.';}
    finally{pending--;}
  }
  updateToolbar();editor.commands.focus('end');
  return DotNet.createJSObjectReference({
    markDirty(){dirty=true;},
    document(){
      if(pending)throw Error('Дождитесь загрузки изображения.');
      const text=JSON.stringify(editor.getJSON());const blob=new Blob([text],{type:'application/json'});
      if(blob.size>128*1024)throw Error('Документ превышает 128 КБ.');
      frozen=true;editor.setEditable(false);for(const b of toolbar.querySelectorAll('button,select'))b.disabled=true;
      return DotNet.createJSStreamReference(blob);
    },
    resume(){frozen=false;editor.setEditable(true);for(const b of toolbar.querySelectorAll('button,select'))b.disabled=false;},
    insertImage(id){if(!frozen&&validId(id))editor.chain().focus().insertContent({type:'image',attrs:{attachmentId:id}}).run();},
    confirmDiscard(){return !pending&&(!dirty||window.confirm('Есть несохранённый текст. Отбросить изменения? Загруженные файлы останутся во вложениях объекта.'));},
    destroy(){destroyed=true;window.removeEventListener('beforeunload',beforeUnload);root?.removeEventListener('input',fieldChanged);root?.removeEventListener('change',fieldChanged);editor.destroy();host.replaceChildren();}
  });
}
