namespace Gameya.Application.Security;

/// <summary>Base of the access declarations. Every request type carries exactly one.</summary>
public abstract class AccessAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PublicAccessAttribute : AccessAttribute;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RequiresAuthAttribute : AccessAttribute;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RequiresRoleAttribute(string role) : AccessAttribute
{
    public string Role { get; } = role;
}

/// <summary>Only the gam'eya's organizer; anyone else gets GAMEYA_NOT_FOUND. The request must implement IGameyaRequest.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RequiresGameyaOrganizerAttribute : AccessAttribute;

/// <summary>The organizer or an Active member; anyone else gets GAMEYA_NOT_FOUND. The request must implement IGameyaRequest.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RequiresGameyaMemberAttribute : AccessAttribute;
