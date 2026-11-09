namespace Gameya.Application.Abstractions;

/// <summary>Keyed one-way hash for OTP codes and opaque tokens (stored only hashed).</summary>
public interface ISecretHasher
{
    string Hash(string value);
}
