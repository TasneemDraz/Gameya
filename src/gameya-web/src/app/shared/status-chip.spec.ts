import { TestBed } from '@angular/core/testing';
import { StatusChip } from './status-chip';

describe('StatusChip', () => {
  it('renders both the icon and the label text', async () => {
    const fixture = TestBed.createComponent(StatusChip);
    fixture.componentRef.setInput('icon', 'check_circle');
    fixture.componentRef.setInput('label', 'Active');
    fixture.componentRef.setInput('tone', 'success');
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('mat-icon')?.textContent?.trim()).toBe('check_circle');
    expect(el.textContent).toContain('Active');
    expect(el.classList.contains('tone-success')).toBe(true);
  });

  it('defaults to the neutral tone', async () => {
    const fixture = TestBed.createComponent(StatusChip);
    fixture.componentRef.setInput('icon', 'info');
    fixture.componentRef.setInput('label', 'x');
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).classList.contains('tone-neutral')).toBe(true);
  });
});
