/** TypeScript mirrors of `GameyaDtos.cs` and the request bodies in `GameyaEndpoints` / `InviteEndpoints`. */
export type GameyaStatus = 'Draft' | 'Recruiting' | 'Active' | 'Completed' | 'Cancelled';
export type GameyaFrequency = 'Monthly' | 'Weekly';
export type PayoutTiming = 'AfterFullCollection' | 'FixedDate';
export type OrderMethod = 'Manual' | 'RandomDraw' | 'ByRequest';
export type GameyaRole = 'None' | 'Member' | 'Organizer';
export type InvitePreviewState = 'Open' | 'Cancelled' | 'Full';
export type InviteRelationship = 'None' | 'Member' | 'RequestPending' | 'Organizer';

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

export interface PendingInvite {
  id: string;
  phone: string;
  shareCount: number;
  registeredName: string | null;
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
