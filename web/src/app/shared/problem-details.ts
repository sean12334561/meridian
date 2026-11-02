import { HttpErrorResponse } from '@angular/common/http';
import { AbstractControl, FormGroup } from '@angular/forms';

/** An error response from the API (RFC 9457). Extension fields are top-level JSON. */
export class Problem {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail: string | null,
    readonly type: string | null,
    readonly errors: Record<string, string[]>,
    readonly body: Record<string, unknown>,
  ) {}

  static from(response: HttpErrorResponse): Problem {
    const body = (typeof response.error === 'object' && response.error) || {};
    const title = body.title ?? (response.status === 0 ? 'The server cannot be reached' : 'Something went wrong');

    return new Problem(response.status, title, body.detail ?? null, body.type ?? null, body.errors ?? {}, body);
  }

  get message(): string {
    return this.detail ?? this.title;
  }
}

/** Shows server validation messages under their fields. Returns messages with no matching field. */
export function applyServerErrors(form: FormGroup, problem: Problem): string[] {
  const unmatched: string[] = [];

  for (const [field, messages] of Object.entries(problem.errors)) {
    const control: AbstractControl | null = form.get(field);
    if (control) {
      control.setErrors({ server: messages.join(' ') });
      control.markAsTouched();
    } else {
      unmatched.push(...messages);
    }
  }

  return unmatched;
}

export function errorMessage(error: unknown): string {
  return error instanceof Problem ? error.message : 'Something went wrong';
}
