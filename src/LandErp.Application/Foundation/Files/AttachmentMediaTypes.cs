namespace LandErp.Application.Foundation.Files;
public static class AttachmentMediaTypes
{
    public static bool IsImage(string contentType)=>contentType.ToLowerInvariant() is "image/jpeg" or "image/png" or "image/gif" or "image/webp" or "image/avif";
    public static bool IsVideo(string contentType)=>contentType.ToLowerInvariant() is "video/mp4" or "video/webm" or "video/quicktime";
    public static bool IsPreviewable(string contentType)=>IsImage(contentType)||IsVideo(contentType);
}