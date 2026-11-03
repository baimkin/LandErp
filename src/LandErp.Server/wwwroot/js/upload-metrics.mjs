// Sliding window speed from browser-reported bytes; excludes provider response/confirmation wait.
export class UploadRate {
  constructor(started,windowMs=3000){this.windowMs=windowMs;this.points=[{at:started,bytes:0}];}
  sample(bytes,at){
    const previous=this.points[this.points.length-1];
    if(!Number.isFinite(bytes)||!Number.isFinite(at)||bytes<previous.bytes||at<previous.at)return 0;
    this.points.push({at,bytes});
    while(this.points.length>2&&this.points[1].at<=at-this.windowMs)this.points.shift();
    const first=this.points[0],elapsed=at-first.at;
    return elapsed>0?Math.max(0,(bytes-first.bytes)*1000/elapsed):0;
  }
}