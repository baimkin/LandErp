// Normalize before Blazor receives input; map the caret by significant characters.
const significant = text => text.replace(/[ \u00a0\u202f₽]/g,'').length;
document.addEventListener('input',event=>{
 const el=event.target;
 if(!(el instanceof HTMLInputElement)||event.isComposing)return;
 const before=el.value,start=el.selectionStart??before.length,end=el.selectionEnd??start;
 let after=before;
 if(el.hasAttribute('data-time-entry')) {
  if(/^\d{4}$/.test(before) && event.inputType!=='deleteContentBackward' && event.inputType!=='deleteContentForward') {
   after=before.slice(0,2)+':'+before.slice(2);el.value=after;el.setSelectionRange(start>2?start+1:start,end>2?end+1:end);
  }
  return;
 }
 if(!el.hasAttribute('data-number-entry'))return;
 const raw=before.replace(/[ \u00a0\u202f₽]/g,'');
 if(!/^-?\d*(?:[.,]\d*)?$/.test(raw))return;
 const [whole,...fraction]=raw.split(/[.,]/);
 after=whole.replace(/\B(?=(\d{3})+(?!\d))/g,' ')+(fraction.length?', '+fraction[0]:'');
 after=after.replace(', ', ',');
 function caret(pos){let needed=significant(before.slice(0,pos)),i=0,count=0;while(i<after.length&&count<needed){if(after[i]!==' ')count++;i++;}return i;}
 if(after!==before){el.value=after;el.setSelectionRange(caret(start),caret(end));}
},true);

// Grow only the explicitly opted-in task field, including prefilled modal drafts.
function growTaskDetails(el){if(el instanceof HTMLTextAreaElement && el.matches('[data-auto-grow]') && el.clientWidth){el.style.height='auto';el.style.height=el.scrollHeight+'px';}}
document.addEventListener('input',event=>growTaskDetails(event.target));
const taskResize=new ResizeObserver(entries=>entries.forEach(e=>{if(e.target.dataset.lastWidth!==String(e.contentRect.width)){e.target.dataset.lastWidth=String(e.contentRect.width);growTaskDetails(e.target);}}));
const taskFields=new Set();
function observeTaskDetails(){for(const el of taskFields){if(!el.isConnected){taskResize.unobserve(el);taskFields.delete(el);}}document.querySelectorAll('textarea[data-auto-grow]').forEach(el=>{if(!taskFields.has(el)){taskFields.add(el);taskResize.observe(el);growTaskDetails(el);}});}
new MutationObserver(observeTaskDetails).observe(document.documentElement,{childList:true,subtree:true});
observeTaskDetails();
function syncSelection(){document.querySelectorAll('input[data-indeterminate]').forEach(el=>{el.indeterminate=el.dataset.indeterminate==='true';});}
new MutationObserver(syncSelection).observe(document.documentElement,{childList:true,subtree:true,attributes:true,attributeFilter:['data-indeterminate']});
syncSelection();