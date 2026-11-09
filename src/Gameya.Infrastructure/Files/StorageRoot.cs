namespace Gameya.Infrastructure.Files;

/// <summary>The resolved local storage directory, exposed so the health check probes the folder the store uses.</summary>
public sealed record StorageRoot(string Path);
