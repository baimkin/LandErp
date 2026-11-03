namespace LandErp.Application.Foundation.Files;

public sealed record FileReadRange(long Offset,long Length);
public interface IStreamingFileStorage
{
    Task<Stream> OpenReadAsync(string storageKey,long expectedSize,string expectedSha256,FileReadRange? range,CancellationToken cancellationToken);
}

// Own the provider response and expose only the requested bytes, never a full in-memory video.
public sealed class FileReadStream(Stream inner,long length,IDisposable? owner=null):Stream
{
    private readonly long totalLength=length;
    private long remaining=length;
    private bool disposed;
    public override bool CanRead=>inner.CanRead;
    public override bool CanSeek=>false;
    public override bool CanWrite=>false;
    public override long Length=>totalLength;
    public override long Position{get=>totalLength-remaining;set=>throw new NotSupportedException();}
    public override int Read(byte[] buffer,int offset,int count)
    {
        if(remaining==0||count==0)return 0;
        int read;
        try{read=inner.Read(buffer,offset,(int)Math.Min(count,remaining));}
        catch(IOException){throw new FileStorageException("STORAGE_UNAVAILABLE",true,"Не удалось прочитать материал.");}
        if(read==0)throw new FileStorageException("STORAGE_INTEGRITY",false,"Материал получен не полностью.");
        remaining-=read;return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
    {
        if(remaining==0||buffer.Length==0)return 0;
        int read;
        try{read=await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length,remaining)],cancellationToken);}
        catch(IOException){throw new FileStorageException("STORAGE_UNAVAILABLE",true,"Не удалось прочитать материал.");}
        if(read==0)throw new FileStorageException("STORAGE_INTEGRITY",false,"Материал получен не полностью.");
        remaining-=read;return read;
    }
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;inner.Dispose();owner?.Dispose();}base.Dispose(disposing);}
    public override async ValueTask DisposeAsync(){if(!disposed){disposed=true;await inner.DisposeAsync();owner?.Dispose();}await base.DisposeAsync();GC.SuppressFinalize(this);}
    public override void Flush()=>throw new NotSupportedException();
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long value)=>throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}