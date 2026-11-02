const jobs = new Map();
const fileAt = (element,index) => { const file=element?.files?.[index]; if(!file)throw new Error('UPLOAD_FILE_UNAVAILABLE'); return file; };
const report = (receiver,id,phase,loaded,total) => receiver.invokeMethodAsync('UploadProgress',id,phase,loaded,total).catch(()=>{});
export function hashFile(element,index,id,receiver,maxBytes) {
  const file=fileAt(element,index);
  if(file.size<=0||file.size>maxBytes)throw new Error('UPLOAD_SIZE');
  const job={cancelled:false};jobs.set(id,job);
  return new Promise((resolve,reject)=>{
    const worker=new Worker(new URL('./file-hash-worker.js',import.meta.url),{type:'module'});job.worker=worker;
    job.rejectHash=()=>{worker.terminate();reject(new Error('UPLOAD_CANCELLED'));};
    worker.onmessage=({data})=>{
      if(data.error){worker.terminate();reject(new Error('UPLOAD_HASH_FAILED'));return;}
      report(receiver,id,'hash',data.loaded,data.total);
      if(data.sha256){worker.terminate();job.worker=null;job.rejectHash=null;resolve(data.sha256);}
    };
    worker.onerror=()=>{worker.terminate();reject(new Error('UPLOAD_HASH_FAILED'));};
    worker.postMessage({file});
  });
}
export function putFile(element,index,url,id,receiver) {
  const file=fileAt(element,index),job=jobs.get(id)||{cancelled:false};jobs.set(id,job);
  if(job.cancelled)throw new Error('UPLOAD_CANCELLED');
  return new Promise((resolve,reject)=>{
    const xhr=new XMLHttpRequest();job.xhr=xhr;
    // Only a transient upload capability reaches the browser. No OAuth, cookies or response-body logging.
    xhr.open('PUT',url);xhr.timeout=30*60*1000;xhr.withCredentials=false;
    xhr.setRequestHeader('Content-Type','application/octet-stream');
    xhr.upload.onprogress=e=>report(receiver,id,'upload',e.loaded,e.lengthComputable?e.total:file.size);
    xhr.onload=()=>{if(xhr.status>=200&&xhr.status<300)resolve();else reject(new Error('UPLOAD_HTTP_'+xhr.status));};
    xhr.onerror=()=>reject(new Error('UPLOAD_NETWORK'));xhr.ontimeout=()=>reject(new Error('UPLOAD_TIMEOUT'));
    xhr.onabort=()=>reject(new Error('UPLOAD_CANCELLED'));xhr.send(file);
  });
}
export function cancel(id) {
  const job=jobs.get(id);if(!job)return;job.cancelled=true;job.rejectHash?.();job.xhr?.abort();
}
export function release(id) { const job=jobs.get(id);job?.worker?.terminate();jobs.delete(id); }