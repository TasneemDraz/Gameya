import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import { catchError, throwError } from 'rxjs';

/** Codes the pages show inline, so a global snackbar would duplicate them. */
const INLINE = /^(OTP_|NAME_INVALID$|VALIDATION_FAILED$|INVALID_PHONE$)/;

/** Set on requests whose failures the caller handles itself (session refresh and startup probing). */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);

export interface ProblemBody {
  code?: string;
  params?: Record<string, unknown>;
  errors?: Record<string, string[]>;
}

export function problemOf(err: unknown): ProblemBody | null {
  return err instanceof HttpErrorResponse && err.error && typeof err.error === 'object'
    ? (err.error as ProblemBody)
    : null;
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const snack = inject(MatSnackBar);
  const transloco = inject(TranslocoService);

  return next(req).pipe(
    catchError((err: unknown) => {
      const problem = problemOf(err);
      const status = err instanceof HttpErrorResponse ? err.status : 0;
      // 401s are handled by the auth interceptor (refresh or redirect); a refresh/session failure is silent.
      if (req.context.get(SILENT_ERRORS)) return throwError(() => err);
      if (status !== 401 && !(problem?.code && INLINE.test(problem.code))) {
        const code =
          problem?.code ??
          (status === 429 ? 'RATE_LIMITED' : status === 413 ? 'FILE_TOO_LARGE' : 'INTERNAL');
        snack.open(transloco.translate(`errors.${code}`, problem?.params ?? {}), undefined, {
          duration: 5000,
        });
      }
      return throwError(() => err);
    }),
  );
};
