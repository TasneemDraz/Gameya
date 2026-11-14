namespace Gameya.Domain.Gameyas;

public enum GameyaStatus { Draft, Recruiting, Active, Completed, Cancelled }

public enum GameyaFrequency { Monthly, Weekly }

public enum PayoutTiming { AfterFullCollection, FixedDate }

public enum OrderMethod { Manual, RandomDraw, ByRequest }

public enum MembershipStatus { Active, Left, Removed }

public enum JoinRequestStatus { Pending, Approved, Rejected, Withdrawn }

public enum InviteStatus { Pending, Accepted, Declined, Revoked }
