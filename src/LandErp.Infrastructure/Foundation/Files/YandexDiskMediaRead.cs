using System.Net;
using LandErp.Application.Foundation.Files;
namespace LandErp.Infrastructure.Foundation.Files;
public sealed partial class YandexDiskFileStorage
{
    public async Task<Stream> OpenReadAsync(string storageKey,long expectedSize,string expectedSha256,FileReadRange? range,CancellationToken cancellationToken)
    {
        string relative=RelativeKey(storageKey),path=options.Root+"/"+relative;
        if(expectedSize<1||expectedSize>options.MaxFileBytes||range is {} r&&(r.Offset<0||r.Length<1||r.Offset>expectedSize-r.Length))throw new ArgumentException("Некорректный диапазон материала.");
        var metadata=await MetadataAsync(path,cancellationToken)??throw Error("STORAGE_NOT_FOUND",false,"Материал не найден.");
        Verify(metadata,expectedSize,expectedSha256);
        if(!string.Equals(Path.GetFileNameWithoutExtension(relative).Split('_')[1],expectedSha256,StringComparison.OrdinalIgnoreCase))throw Error("STORAGE_INTEGRITY",false,"Проверка материала не пройдена.");
        using var link=await ApiJsonAsync(HttpMethod.Get,"/download",path,"",cancellationToken);
        var response=await DownloadAsync(TransferUri(link,"GET"),cancellationToken,range);
        try
        {
            RequireSuccess(response);
            long length=range?.Length??expectedSize;
            if(response.Content.Headers.ContentLength is long actual&&actual!=length)throw Error("STORAGE_INTEGRITY",false,"Размер ответа хранилища не совпал.");
            if(range!=null)
            {
                var cr=response.Content.Headers.ContentRange;
                if(response.StatusCode!=HttpStatusCode.PartialContent||cr?.Unit!="bytes"||cr.From!=range.Offset||cr.To!=range.Offset+range.Length-1||cr.Length!=expectedSize)
                    throw Error("STORAGE_RESPONSE_INVALID",false,"Хранилище не подтвердило диапазон материала.");
            }
            else if(response.StatusCode!=HttpStatusCode.OK)throw Error("STORAGE_RESPONSE_INVALID",false,"Некорректный ответ хранилища.");
            return new FileReadStream(await response.Content.ReadAsStreamAsync(cancellationToken),length,response);
        }
        catch{response.Dispose();throw;}
    }
}