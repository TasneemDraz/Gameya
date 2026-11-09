using Gameya.Domain.Users;

namespace Gameya.Application.Security;

/// <summary>The single place that decides whether a viewer sees a full or masked phone number.</summary>
public static class PhoneDisplay
{
    public static DisplayPhone For(Guid viewerId, Guid subjectId, PhoneNumber phone, bool viewerOrganizesSubjectsGameya) =>
        new(viewerId == subjectId || viewerOrganizesSubjectsGameya ? phone.E164 : phone.Masked);
}
