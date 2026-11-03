import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { OtpInput } from './otp-input';

function setup() {
  TestBed.configureTestingModule({
    imports: [
      TranslocoTestingModule.forRoot({
        langs: { ar: {}, en: {} },
        translocoConfig: { defaultLang: 'ar' },
      }),
    ],
  });
  const fixture = TestBed.createComponent(OtpInput);
  fixture.detectChanges();
  const boxes = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('input'));
  const completed: string[] = [];
  fixture.componentInstance.completed.subscribe((c) => completed.push(c));
  return { fixture, boxes, completed };
}

function paste(el: HTMLInputElement, text: string) {
  const ev = new Event('paste', { bubbles: true, cancelable: true }) as Event & {
    clipboardData: unknown;
  };
  ev.clipboardData = { getData: () => text };
  el.dispatchEvent(ev);
}

describe('OtpInput', () => {
  it('renders 6 boxes with one-time-code autocomplete', () => {
    const { boxes } = setup();
    expect(boxes).toHaveLength(6);
    expect(boxes[0].getAttribute('autocomplete')).toBe('one-time-code');
  });

  it('fills all boxes from a pasted Arabic-Indic code and emits 456123', () => {
    const { boxes, completed } = setup();
    paste(boxes[0], '٤٥٦١٢٣');
    expect(boxes.map((b) => b.value).join('')).toBe('456123');
    expect(completed).toEqual(['456123']);
  });

  it('auto-advances on typing and goes back on backspace', () => {
    const { boxes, completed } = setup();
    boxes[0].focus();
    boxes[0].value = '1';
    boxes[0].dispatchEvent(new Event('input', { bubbles: true }));
    expect(document.activeElement).toBe(boxes[1]);
    boxes[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'Backspace', bubbles: true }));
    expect(document.activeElement).toBe(boxes[0]);
    expect(completed).toEqual([]);
  });
});
