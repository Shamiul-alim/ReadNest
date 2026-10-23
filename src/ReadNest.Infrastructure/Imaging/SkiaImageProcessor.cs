using ReadNest.Application.Abstractions;
using SkiaSharp;

namespace ReadNest.Infrastructure.Imaging;

/// <summary>
/// Profile photo normalisation with SkiaSharp. The image type is identified from the file signature and must
/// agree with what the decoder finds; dimensions are checked from the header before any pixels are decoded, so a
/// small file that claims a huge canvas (a decompression bomb) is rejected cheaply. The output is a fresh WebP
/// encode, which drops EXIF/GPS and any other embedded metadata.
/// </summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    /// <summary>Largest side accepted in the file header.</summary>
    public const int MaxSide = 12_000;

    /// <summary>Largest canvas accepted in the file header (100 megapixels).</summary>
    public const long MaxHeaderPixels = 100_000_000;

    /// <summary>Largest bitmap actually decoded (25 megapixels, about 100 MB of RGBA).</summary>
    public const long MaxDecodedPixels = 25_000_000;

    private const string Unsupported = "Upload a JPEG, PNG or WebP image.";

    public byte[] NormalizeProfilePhoto(ReadOnlySpan<byte> content, int size)
    {
        var declared = Sniff(content) ?? throw new InvalidImageException(Unsupported);

        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data) ?? throw new InvalidImageException("This image could not be read. It may be damaged.");
        if (codec.EncodedFormat != declared)
        {
            throw new InvalidImageException(Unsupported);
        }

        var header = codec.Info;
        if (header.Width <= 0 || header.Height <= 0)
        {
            throw new InvalidImageException("This image could not be read. It may be damaged.");
        }

        if (header.Width > MaxSide || header.Height > MaxSide || (long)header.Width * header.Height > MaxHeaderPixels)
        {
            throw new InvalidImageException("This image's dimensions are too large. Use a photo under 12,000 pixels on each side.");
        }

        // JPEG (and WebP) can decode at a reduced scale, which keeps memory low for large phone photos.
        var shortSide = Math.Min(header.Width, header.Height);
        var scale = Math.Min(1f, Math.Max(size * 2f, 1f) / shortSide);
        var decodeSize = codec.GetScaledDimensions(scale);
        if ((long)decodeSize.Width * decodeSize.Height > MaxDecodedPixels)
        {
            throw new InvalidImageException("This image has too many pixels to process. Use a smaller photo.");
        }

        var decodeInfo = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (result != SKCodecResult.Success)
        {
            throw new InvalidImageException("This image could not be read. It may be damaged or incomplete.");
        }

        using var oriented = Orient(decoded, codec.EncodedOrigin);
        using var source = SKImage.FromBitmap(oriented);

        var side = Math.Min(source.Width, source.Height);
        var target = Math.Min(size, side);
        var crop = SKRect.Create((source.Width - side) / 2f, (source.Height - side) / 2f, side, side);

        using var surface = SKSurface.Create(new SKImageInfo(target, target, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidImageException("This image could not be processed.");
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(source, crop, SKRect.Create(0, 0, target, target), new SKSamplingOptions(SKCubicResampler.Mitchell), null);
        surface.Canvas.Flush();

        using var output = surface.Snapshot();
        using var encoded = output.Encode(SKEncodedImageFormat.Webp, 85)
            ?? throw new InvalidImageException("This image could not be processed.");
        return encoded.ToArray();
    }

    /// <summary>Identifies JPEG, PNG and WebP by their magic bytes. Everything else (SVG, GIF, HEIC ...) is null.</summary>
    public static SKEncodedImageFormat? Sniff(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return SKEncodedImageFormat.Jpeg;
        }

        if (content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return SKEncodedImageFormat.Png;
        }

        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
        {
            return SKEncodedImageFormat.Webp;
        }

        return null;
    }

    /// <summary>Applies the EXIF orientation so photos taken sideways on a phone appear upright.</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;
        var result = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(width, height);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, height);
                canvas.RotateDegrees(270);
                break;
        }

        canvas.DrawBitmap(source, 0, 0);
        return result;
    }
}
