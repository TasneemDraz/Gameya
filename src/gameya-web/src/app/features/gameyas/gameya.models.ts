/** TypeScript mirrors of `GameyaDtos.cs` and the request bodies in `GameyaEndpoints` / `InviteEndpoints`. */
export type GameyaStatus = 'Draft' | 'Recruiting' | 'Active' | 'Completed' | 'Cancelled';
export type GameyaFrequency = 'Monthly' | 'Weekly';
export type PayoutTiming = 'AfterFullCollection' | 'FixedDate';
export type OrderMethod = 'Manual' | 'RandomDraw' | 'ByRequest';
export type GameyaRole = 'None' | 'Member' | 'Organizer';
export type InvitePreviewState = 'Open' | 'Cancelled' | 'Full';
export type InviteRelationship = 'None' | 'Member' | 'Invited' | 'RequestPending' | 'Organizer';
export type JoinRequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Withdrawn';

/** Dates are `yyyy-MM-dd`; timestamps are ISO 8601. */
export interface InvitePreview {
  gameyaId: string;
  state: InvitePreviewState;
  name: string;
  description: string | null;
  contributionAmount: number;
  currency: string;
  shareCount: number;
  frequency: GameyaFrequency;
  startDate: string;
  endDate: string;
  pot: number;
  graceDays: number;
  payoutTiming: PayoutTiming;
  payoutOffsetDays: number | null;
  orderMethod: OrderMethod;
  maxSharesPerMember: number;
  availableShares: number;
  rulesText: string | null;
  organizerName: string | null;
  relationship: InviteRelationship | null;
  requestNeedsReaccept: boolean;
  /** The viewer's own pending direct invite; set only when `relationship` is `Invited`. */
  inviteId: string | null;
}

export interface MyGameya {
  id: string;
  name: string;
  status: GameyaStatus;
  role: GameyaRole;
  contributionAmount: number;
  shareCount: number;
  myShareCount: number;
  needsReaccept: boolean;
}

export interface MyInvite {
  inviteId: string;
  gameyaId: string;
  name: string;
  contributionAmount: number;
  shareCount: number;
  roundCount: number;
  invitedShares: number;
  orderMethod: OrderMethod;
  organizerName: string | null;
}

/** The caller's own pending request to join (`MyJoinRequestDto`). */
export interface MyJoinRequest {
  gameyaId: string;
  name: string;
  status: JoinRequestStatus;
  contributionAmount: number;
  gameyaShareCount: number;
  shareCount: number;
  needsReaccept: boolean;
}

export interface TermsDiff {
  oldContributionAmount: number | null;
  oldShareCount: number | null;
  oldFrequency: GameyaFrequency | null;
}

export interface Gameya {
  id: string;
  name: string;
  description: string | null;
  contributionAmount: number;
  shareCount: number;
  frequency: GameyaFrequency;
  startDate: string;
  graceDays: number;
  payoutTiming: PayoutTiming;
  payoutOffsetDays: number | null;
  orderMethod: OrderMethod;
  maxSharesPerMember: number;
  organizerParticipates: boolean;
  rulesText: string | null;
  status: GameyaStatus;
  termsVersion: number;
  currency: string;
  pot: number;
  roundCount: number;
  endDate: string;
  totalPerShare: number;
  assignedShares: number;
  reservedShares: number;
  availableShares: number;
  myRole: GameyaRole;
  myShareCount: number;
  needsReaccept: boolean;
  inviteCode: string | null;
  pendingJoinRequestCount: number | null;
  termsDiff: TermsDiff | null;
}

export interface GameyaMember {
  userId: string;
  fullName: string | null;
  photoUrl: string | null;
  phone: string | null;
  shareCount: number;
  isOrganizer: boolean;
  needsReaccept: boolean;
}

export interface ActivityEntry {
  occurredAt: string;
  actorName: string | null;
  action: string;
  details: Record<string, unknown> | null;
  note: string | null;
}

export interface ActivityPage {
  items: ActivityEntry[];
  page: number;
  hasMore: boolean;
}

export interface JoinRequest {
  id: string;
  userId: string;
  fullName: string | null;
  phone: string | null;
  sharePreferences: number[][];
  termsCurrent: boolean;
}

/** Only the phone the organizer typed: who it belongs to stays private until the invite is accepted. */
export interface PendingInvite {
  id: string;
  phone: string;
  shareCount: number;
}

/** The create and edit form body (`GameyaSettingsBody`). */
export interface GameyaSettingsBody {
  name: string;
  description: string | null;
  contributionAmount: number;
  shareCount: number;
  frequency: GameyaFrequency;
  startDate: string;
  graceDays: number;
  payoutTiming: PayoutTiming;
  payoutOffsetDays: number | null;
  orderMethod: OrderMethod;
  maxSharesPerMember: number;
  organizerParticipates: boolean;
  rulesText: string | null;
  organizerShares: number;
}

/** One entry per share; `preferences` are 1-based round numbers (empty = no preference). */
export interface ShareBody {
  preferences: number[];
}

/** The join and accept-invite body (`JoinBody`). */
export interface JoinBody {
  shares: ShareBody[];
  acceptRules: boolean;
  acceptNoMoney: boolean;
}
