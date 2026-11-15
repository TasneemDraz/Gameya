import { StatusTone } from '../../shared/status-chip';
import { GameyaStatus } from './gameya.models';

export function gameyaStatusChip(status: GameyaStatus): {
  icon: string;
  labelKey: string;
  tone: StatusTone;
} {
  switch (status) {
    case 'Draft':
      return { icon: 'edit_note', labelKey: 'gameyaStatus.Draft', tone: 'neutral' };
    case 'Recruiting':
      return { icon: 'group_add', labelKey: 'gameyaStatus.Recruiting', tone: 'info' };
    case 'Active':
      return { icon: 'autorenew', labelKey: 'gameyaStatus.Active', tone: 'success' };
    case 'Completed':
      return { icon: 'task_alt', labelKey: 'gameyaStatus.Completed', tone: 'success' };
    case 'Cancelled':
      return { icon: 'cancel', labelKey: 'gameyaStatus.Cancelled', tone: 'danger' };
  }
}
