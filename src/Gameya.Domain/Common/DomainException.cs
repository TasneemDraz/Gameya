namespace Gameya.Domain.Common;

public class DomainException(
    string code,
    IReadOnlyDictionary<string, object?>? @params = null,
    bool commitStateChanges = false) : Exception(code)
{
    public string Code { get; } = code;

    public IReadOnlyDictionary<string, object?>? Params { get; } = @params;

    /// <summary>True when state changed by the throwing operation must be persisted despite the error.</summary>
    public bool CommitStateChanges { get; } = commitStateChanges;
}
