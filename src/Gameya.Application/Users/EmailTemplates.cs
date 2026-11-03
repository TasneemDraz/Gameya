using System.Net;
using Gameya.Application.Abstractions;
using Gameya.Domain.Users;

namespace Gameya.Application.Users;

public static class EmailTemplates
{
    public static EmailMessage VerifyEmail(Language language, string to, string link)
    {
        var (subject, intro, action, ignore) = language switch
        {
            Language.Ar => (
                "تأكيد بريدك الإلكتروني في جمعية",
                "اضغط على الرابط التالي لتأكيد بريدك الإلكتروني. الرابط صالح لمدة 24 ساعة.",
                "تأكيد البريد الإلكتروني",
                "إذا لم تطلب ذلك، يمكنك تجاهل هذه الرسالة."),
            _ => (
                "Confirm your Gam'eya email",
                "Use the link below to confirm your email address. It is valid for 24 hours.",
                "Confirm email",
                "If you did not request this, you can ignore this message.")
        };
        var dir = language == Language.Ar ? "rtl" : "ltr";
        var encodedLink = WebUtility.HtmlEncode(link);
        var html = $"<html dir=\"{dir}\"><body dir=\"{dir}\"><p>{intro}</p>"
                   + $"<p><a href=\"{encodedLink}\">{action}</a></p><p>{ignore}</p></body></html>";
        var text = $"{intro}\n\n{link}\n\n{ignore}";
        return new EmailMessage(to, subject, html, text);
    }
}
