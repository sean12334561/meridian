import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { CurrentUserService } from '../user/current-user.service';

export const userIdInterceptor: HttpInterceptorFn = (request, next) => {
  const id = inject(CurrentUserService).id();

  return next(id === null ? request : request.clone({ setHeaders: { 'X-User-Id': String(id) } }));
};
