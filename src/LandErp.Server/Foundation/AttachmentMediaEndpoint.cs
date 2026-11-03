using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Server.Security;
using Microsoft.Net.Http.Headers;
namespace LandErp.Server.Foundation;
internal static class AttachmentMediaEndpoint
{
    public static async Task<IResult> ReadAsync(Guid id,HttpContext http,IProcurementWorkspace workspace,CancellationToken token)
    {
        http.Response.Headers.CacheControl="no-store";
        http.Response.Headers.XContentTypeOptions="nosniff";
        http.Response.Headers["Referrer-Policy"]="no-referrer";
        var subject=PermissionAuthorization.SubjectFrom(http.User);
        var info=await workspace.ReadAttachmentMediaInfoAsync(subject,id,token);
        string etag="\""+info.Sha256+"\"";
        http.Response.Headers.ETag=etag;http.Response.Headers.AcceptRanges="bytes";
        FileReadRange? range=null;
        string header=http.Request.Headers.Range.ToString();
        if(header.Length>0&&(http.Request.Headers.IfRange.Count==0||http.Request.Headers.IfRange.ToString()==etag))
        {
            if(!TryRange(header,info.SizeBytes,out range))
            {http.Response.Headers.ContentRange=$"bytes */{info.SizeBytes}";return Results.StatusCode(416);}
        }
        Stream stream=await workspace.OpenAttachmentMediaAsync(subject,id,range,token);
        if(range!=null)
        {
            http.Response.StatusCode=206;
            http.Response.Headers.ContentRange=$"bytes {range.Offset}-{range.Offset+range.Length-1}/{info.SizeBytes}";
        }
        http.Response.ContentLength=range?.Length??info.SizeBytes;
        return Results.Stream(stream,info.ContentType);
    }
    internal static bool TryRange(string value,long size,out FileReadRange? result)
    {
        result=null;
        if(!RangeHeaderValue.TryParse(value,out var parsed)||parsed.Unit!="bytes"||parsed.Ranges.Count!=1)return false;
        var range=parsed.Ranges.Single();long start,end;
        if(range.From is long from){start=from;end=Math.Min(range.To??(size-1),size-1);}
        else if(range.To is long suffix&&suffix>0){start=Math.Max(0,size-suffix);end=size-1;}
        else return false;
        if(start<0||start>=size||end<start)return false;
        result=new(start,end-start+1);return true;
    }
}