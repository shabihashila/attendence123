namespace FegHR.SharedKernel.Http;

/// <summary>
/// Recognises an uploaded image by its bytes. The browser's content type is only a claim; a
/// file is stored as an image when its signature says it is one.
/// </summary>
public static class ImageFiles
{
    /// <summary>The content type and extension of a PNG, JPEG or WebP image, or null.</summary>
    public static (string ContentType, string Extension)? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return ("image/png", ".png");
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ("image/jpeg", ".jpg");
        if (data.Length >= 16 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
            return ("image/webp", ".webp");
        return null;
    }
}
