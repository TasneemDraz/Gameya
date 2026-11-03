using Gameya.Application.Abstractions;
using Gameya.Domain.Common;
using ImageMagick;

namespace Gameya.Infrastructure.Files;

/// <summary>
/// Accepts JPEG, PNG and HEIC by content. The container signature is checked first so ImageMagick's other
/// decoders (and their delegates) never see untrusted bytes; Magick then confirms the format.
/// </summary>
public sealed class MagickImageProcessor : IImageProcessor
{
    private const uint MaxPixels = 50_000_000;
    private const int JpegQuality = 85;

    private static readonly string[] HeicBrands = ["heic", "heix", "hevc", "hevx", "heim", "heis", "mif1", "msf1"];

    public Stream ToJpeg(Stream input, int maxSize)
    {
        var data = new MemoryStream();
        input.CopyTo(data);

        var format = Sniff(data.GetBuffer().AsSpan(0, (int)data.Length)) ?? throw NotAllowed();

        try
        {
            data.Position = 0;
            var info = new MagickImageInfo(data, new MagickReadSettings { Format = format });
            if (info.Format is not (MagickFormat.Jpeg or MagickFormat.Jpg or MagickFormat.Png or MagickFormat.Heic or MagickFormat.Heif)
                || (ulong)info.Width * info.Height > MaxPixels)
            {
                throw NotAllowed();
            }

            data.Position = 0;
            using var image = new MagickImage(data, new MagickReadSettings { Format = format });
            image.AutoOrient();
            image.Strip();
            image.Resize(new MagickGeometry((uint)maxSize, (uint)maxSize) { Greater = true });
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
            image.Format = MagickFormat.Jpeg;
            image.Quality = JpegQuality;

            var output = new MemoryStream();
            image.Write(output);
            output.Position = 0;
            return output;
        }
        catch (MagickException)
        {
            throw NotAllowed();
        }
    }

    private static MagickFormat? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
        {
            return MagickFormat.Jpeg;
        }

        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return MagickFormat.Png;
        }

        if (b.Length >= 12 && b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p')
        {
            var brand = System.Text.Encoding.ASCII.GetString(b.Slice(8, 4));
            return HeicBrands.Contains(brand) ? MagickFormat.Heic : null;
        }

        return null;
    }

    private static DomainException NotAllowed() => new(ErrorCodes.FILE_TYPE_NOT_ALLOWED);
}
