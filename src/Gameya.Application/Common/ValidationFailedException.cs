namespace Gameya.Application.Common;

public sealed class ValidationFailedException(IDictionary<string, string[]> errors)
    : Exception("VALIDATION_FAILED")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
