import { HttpErrorResponse } from '@angular/common/http';
import { FormControl, FormGroup } from '@angular/forms';
import { describe, expect, it } from 'vitest';
import { Problem, applyServerErrors, errorMessage } from './problem-details';

describe('Problem.from', () => {
  it('reads the fields and keeps extensions in the body', () => {
    const problem = Problem.from(new HttpErrorResponse({
      status: 409,
      error: { title: 'Conflict', detail: 'Already decided', current: { id: 1 } },
    }));

    expect(problem.message).toBe('Already decided');
    expect(problem.body['current']).toEqual({ id: 1 });
  });

  it('copes with a body that is not JSON, such as an nginx error page', () => {
    const problem = Problem.from(new HttpErrorResponse({ status: 502, error: '<html></html>' }));

    expect(problem.message).toBe('Something went wrong');
  });

  it('says when the server cannot be reached', () => {
    expect(Problem.from(new HttpErrorResponse({ status: 0 })).message).toBe('The server cannot be reached');
  });
});

describe('applyServerErrors', () => {
  it('puts messages on their fields and returns the rest', () => {
    const form = new FormGroup({ weight: new FormControl(7) });
    const problem = new Problem(400, 'Invalid', null, null, { weight: ['Too high.'], other: ['Elsewhere.'] }, {});

    const unmatched = applyServerErrors(form, problem);

    expect(form.controls.weight.getError('server')).toBe('Too high.');
    expect(unmatched).toEqual(['Elsewhere.']);
  });
});

describe('errorMessage', () => {
  it('hides anything that is not a Problem', () => {
    expect(errorMessage(new Error('Internal detail'))).toBe('Something went wrong');
  });
});
