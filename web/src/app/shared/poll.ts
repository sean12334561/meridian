import { Observable, catchError, exhaustMap, from, map, of, takeWhile, timer } from 'rxjs';
import { Problem } from './problem-details';

export interface PollResult<T> {
  value?: T;
  error?: unknown;
}

/**
 * Calls load straight away and then every period, until isFinal says stop. A failed call is emitted
 * and polling goes on, so one bad response doesn't freeze the screen. A slow call isn't overlapped.
 */
export function poll<T>(
  load: () => Promise<T>,
  isFinal: (result: PollResult<T>) => boolean,
  periodMs = 1000,
): Observable<PollResult<T>> {
  return timer(0, periodMs).pipe(
    exhaustMap(() =>
      from(load()).pipe(
        map((value): PollResult<T> => ({ value })),
        catchError((error: unknown) => of<PollResult<T>>({ error })),
      ),
    ),
    takeWhile((result) => !isFinal(result), true),
  );
}

/** A 4xx won't get better by asking again. Anything else may be a restart or a network blip. */
export function isPermanentFailure(error: unknown): boolean {
  return error instanceof Problem && error.status >= 400 && error.status < 500;
}
