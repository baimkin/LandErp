import { Sha256 } from './sha256.mjs';
self.onmessage = async ({data}) => {
  try {
    const file=data.file, hash=new Sha256(); let loaded=0, last=0;
    // File.slice keeps memory bounded on browsers without streaming WebCrypto digests.
    while(loaded<file.size){
      const bytes=new Uint8Array(await file.slice(loaded,loaded+1024*1024).arrayBuffer());
      hash.update(bytes);loaded+=bytes.length;
      if(performance.now()-last>150||loaded===file.size){self.postMessage({loaded,total:file.size});last=performance.now();}
    }
    self.postMessage({sha256:hash.digest(),loaded,total:file.size});
  } catch { self.postMessage({error:true}); }
};