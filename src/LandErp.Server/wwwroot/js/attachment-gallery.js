const observers=new WeakMap(),dialogs=new WeakMap();
export function observe(root){
  root.dataset.galleryReady="true";
  disconnect(root);
  const observer=new IntersectionObserver(entries=>{for(const entry of entries){if(!entry.isIntersecting)continue;const video=entry.target;if(!video.src){video.addEventListener('loadedmetadata',()=>{if(Number.isFinite(video.duration)&&video.duration>0)video.currentTime=Math.min(0.1,video.duration/2);},{once:true});video.src=video.dataset.preview;}observer.unobserve(video);}}, {rootMargin:'100px'});
  root.querySelectorAll('video[data-preview]:not([src])').forEach(video=>observer.observe(video));observers.set(root,observer);
}
export function disconnect(root){observers.get(root)?.disconnect();observers.delete(root);}
export function stop(dialog){dialog.querySelectorAll('video').forEach(video=>{video.pause();video.removeAttribute('src');video.load();});}
export function open(dialog,receiver){
  const previous=document.activeElement;
  const cancel=event=>{event.preventDefault();receiver.invokeMethodAsync('CloseAsync').catch(()=>{});};
  const key=event=>{
    if(event.target.closest('video,input,textarea'))return;
    if(event.key==='ArrowLeft'||event.key==='ArrowRight'){event.preventDefault();receiver.invokeMethodAsync('NavigateAsync',event.key==='ArrowLeft'?-1:1).catch(()=>{});}
    else if(event.key==='+'||event.key==='-'){event.preventDefault();receiver.invokeMethodAsync('ZoomAsync',event.key==='+'?1:-1).catch(()=>{});}
  };
  dialog.addEventListener('cancel',cancel);dialog.addEventListener('keydown',key);dialogs.set(dialog,{previous,cancel,key});
  if(!dialog.open)dialog.showModal();dialog.querySelector('.viewer-close')?.focus();
}
export function close(dialog){
  stop(dialog);const state=dialogs.get(dialog);if(state){dialog.removeEventListener('cancel',state.cancel);dialog.removeEventListener('keydown',state.key);}
  if(dialog.open)dialog.close();if(state?.previous?.isConnected)state.previous.focus();dialogs.delete(dialog);
}
export function center(dialog){const viewport=dialog.querySelector('.photo-viewport');if(viewport){viewport.scrollLeft=(viewport.scrollWidth-viewport.clientWidth)/2;viewport.scrollTop=(viewport.scrollHeight-viewport.clientHeight)/2;}}

export function pauseFeatured(root){root.querySelector('.featured-video')?.pause();}
export function stopFeatured(root){const video=root.querySelector('.featured-video');if(video){video.pause();video.removeAttribute('src');video.load();}}
export function scrollThumbs(root,direction){const rail=root.querySelector(".featured-thumbnails");if(rail)rail.scrollLeft=Math.max(0,Math.min(rail.scrollWidth-rail.clientWidth,rail.scrollLeft+Math.sign(direction)*rail.clientWidth*.75));}
export function revealThumb(root){const rail=root.querySelector('.featured-thumbnails'),thumb=rail?.querySelector('[data-active=true]');if(!thumb)return;const r=rail.getBoundingClientRect(),t=thumb.getBoundingClientRect();if(t.left<r.left)rail.scrollLeft-=r.left-t.left;else if(t.right>r.right)rail.scrollLeft+=t.right-r.right;}