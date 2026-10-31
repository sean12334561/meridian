import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PollResult, isPermanentFailure, poll } from './poll';
import { Problem } from './problem-details';

function problem(status: number): Problem {
  return new Problem(status, 'Failed', null, null, {}, {});
}

describe('poll', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('keeps going after a failed call and stops at the final value', async () => {
    const load = vi.fn<() => Promise<string>>()
      .mockRejectedValueOnce(problem(502))
      .mockResolvedValueOnce('Running')
      .mockResolvedValueOnce('Completed');
    const results: PollResult<string>[] = [];

    poll(load, (result) => result.value === 'Completed', 1000).subscribe((result) => results.push(result));
    await vi.advanceTimersByTimeAsync(5000);

    expect(results.map((result) => result.value)).toEqual([undefined, 'Running', 'Completed']);
    expect(load).toHaveBeenCalledTimes(3);
  });

  it('does not call again while a call is still running', async () => {
    let finish: (value: string) => void = () => undefined;
    const load = vi.fn(() => new Promise<string>((resolve) => (finish = resolve)));

    const subscription = poll(load, () => false, 1000).subscribe();
    await vi.advanceTimersByTimeAsync(3500);

    expect(load).toHaveBeenCalledTimes(1);
    finish('done');
    subscription.unsubscribe();
  });

  it('stops calling once unsubscribed', async () => {
    const load = vi.fn(() => Promise.resolve('Running'));

    const subscription = poll(load, () => false, 1000).subscribe();
    await vi.advanceTimersByTimeAsync(1500);
    subscription.unsubscribe();
    await vi.advanceTimersByTimeAsync(5000);

    expect(load).toHaveBeenCalledTimes(2);
  });
});

describe('isPermanentFailure', () => {
  it('gives up on a 4xx only', () => {
    expect(isPermanentFailure(problem(404))).toBe(true);
    expect(isPermanentFailure(problem(502))).toBe(false);
    expect(isPermanentFailure(problem(0))).toBe(false);
    expect(isPermanentFailure(new Error('Other'))).toBe(false);
  });
});
