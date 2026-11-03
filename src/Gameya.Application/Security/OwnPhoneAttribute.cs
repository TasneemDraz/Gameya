namespace Gameya.Application.Security;

/// <summary>
/// Marks a response property that carries the caller's own raw phone number. It is the only sanctioned way
/// for a raw phone to leave the application; every other phone is masked.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class OwnPhoneAttribute : Attribute;
