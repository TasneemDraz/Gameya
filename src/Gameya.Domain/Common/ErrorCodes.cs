namespace Gameya.Domain.Common;

public static class ErrorCodes
{
    public const string VALIDATION_FAILED = "VALIDATION_FAILED";
    public const string UNAUTHENTICATED = "UNAUTHENTICATED";
    public const string FORBIDDEN = "FORBIDDEN";
    public const string NOT_FOUND = "NOT_FOUND";
    public const string RATE_LIMITED = "RATE_LIMITED";
    public const string INVALID_PHONE = "INVALID_PHONE";
    public const string OTP_INVALID = "OTP_INVALID";
    public const string OTP_EXPIRED = "OTP_EXPIRED";
    public const string OTP_LOCKED = "OTP_LOCKED";
    public const string OTP_RESEND_TOO_SOON = "OTP_RESEND_TOO_SOON";
    public const string OTP_RESEND_LIMIT = "OTP_RESEND_LIMIT";
    public const string REFRESH_TOKEN_INVALID = "REFRESH_TOKEN_INVALID";
    public const string REFRESH_TOKEN_REUSED = "REFRESH_TOKEN_REUSED";
    public const string USER_BLOCKED = "USER_BLOCKED";
    public const string NAME_INVALID = "NAME_INVALID";
    public const string EMAIL_TOKEN_INVALID = "EMAIL_TOKEN_INVALID";
    public const string EMAIL_TOKEN_EXPIRED = "EMAIL_TOKEN_EXPIRED";
    public const string EMAIL_RESEND_LIMIT = "EMAIL_RESEND_LIMIT";
    public const string EMAIL_IN_USE = "EMAIL_IN_USE";
    public const string ACCOUNT_HAS_ACTIVE_GAMEYAS = "ACCOUNT_HAS_ACTIVE_GAMEYAS";
    public const string FILE_TOO_LARGE = "FILE_TOO_LARGE";
    public const string FILE_TYPE_NOT_ALLOWED = "FILE_TYPE_NOT_ALLOWED";
    public const string CONCURRENCY_CONFLICT = "CONCURRENCY_CONFLICT";
    public const string SMS_SEND_FAILED = "SMS_SEND_FAILED";
}
