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
    public const string GAMEYA_NOT_FOUND = "GAMEYA_NOT_FOUND";
    public const string INVALID_STATUS_TRANSITION = "INVALID_STATUS_TRANSITION";
    public const string NOT_ENOUGH_SHARES = "NOT_ENOUGH_SHARES";
    public const string MAX_SHARES_EXCEEDED = "MAX_SHARES_EXCEEDED";
    public const string SHARES_BELOW_ASSIGNED = "SHARES_BELOW_ASSIGNED";
    public const string TERMS_NOT_ACCEPTED = "TERMS_NOT_ACCEPTED";
    public const string INVITE_CODE_INVALID = "INVITE_CODE_INVALID";
    public const string GAMEYA_FULL = "GAMEYA_FULL";
    public const string ALREADY_MEMBER = "ALREADY_MEMBER";
    public const string REQUEST_ALREADY_PENDING = "REQUEST_ALREADY_PENDING";
    public const string INVITE_NOT_FOUND = "INVITE_NOT_FOUND";
    public const string ORGANIZER_CANNOT_LEAVE = "ORGANIZER_CANNOT_LEAVE";
    public const string START_DATE_IN_PAST = "START_DATE_IN_PAST";
    public const string INVALID_PREFERENCES = "INVALID_PREFERENCES";
    public const string INVITEE_REQUEST_PENDING = "INVITEE_REQUEST_PENDING";
    public const string REQUESTER_ACCOUNT_DELETED = "REQUESTER_ACCOUNT_DELETED";
}
