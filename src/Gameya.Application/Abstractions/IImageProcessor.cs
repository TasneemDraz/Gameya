namespace Gameya.Application.Abstractions;

public interface IImageProcessor
{
    /// <summary>
    /// Sniffs the content (not the file name) and converts JPEG, PNG or HEIC to a JPEG whose longest side is
    /// at most <paramref name="maxSize"/>. Throws FILE_TYPE_NOT_ALLOWED for anything else, including corrupt input.
    /// </summary>
    Stream ToJpeg(Stream input, int maxSize);
}
