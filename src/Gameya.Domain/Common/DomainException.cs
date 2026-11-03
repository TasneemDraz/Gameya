namespace Gameya.Domain.Common;

public class DomainException(string code, IReadOnlyDictionary<string, object?>? @params = null) : Exception(code)
{
    public string Code { get; } = code;

    public IReadOnlyDictionary<string, object?>? Params { get; } = @params;
}
