using Gameya.Domain.Users;

namespace Gameya.Application.Auth;

public static class SmsTemplates
{
    public static string Otp(Language language, string code) => language switch
    {
        Language.Ar => $"رمز الدخول إلى جمعية: {code}. صالح لمدة 5 دقائق.",
        _ => $"Your Gam'eya login code is {code}. Valid for 5 minutes."
    };
}
