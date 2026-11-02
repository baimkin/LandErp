import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { Sha256 } from '../src/LandErp.Server/wwwroot/js/sha256.mjs';
let checks=0;
for(const text of ['', 'abc', 'abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq']){
  const bytes=new TextEncoder().encode(text);
  assert.equal(new Sha256().update(bytes).digest(),createHash('sha256').update(bytes).digest('hex'));checks++;
}
for(const length of [1,55,56,63,64,65,127,128,129,1025]){
  const bytes=Uint8Array.from({length},(_,i)=>(i*17+9)&255);
  for(const step of [1,7,64,513]){
    const h=new Sha256();for(let p=0;p<length;p+=step)h.update(bytes.subarray(p,p+step));
    assert.equal(h.digest(),createHash('sha256').update(bytes).digest('hex'));checks++;
  }
}
// Full 500 MiB length, including padding's encoded bit length. Reuse one chunk, never allocate the file.
const chunk=new Uint8Array(1024*1024);const custom=new Sha256(),native=createHash('sha256');
for(let i=0;i<500;i++){custom.update(chunk);native.update(chunk);}
assert.equal(custom.digest(),native.digest('hex'));checks++;
assert.throws(()=>custom.update(chunk));assert.throws(()=>custom.digest());checks+=2;
console.log(JSON.stringify({Passed:checks,StreamedBytes:500*1024*1024,ReusableChunkBytes:chunk.length}));