import { HttpInterceptorFn } from '@angular/common/http';

export const correlationIdInterceptor: HttpInterceptorFn = (request, next) => {
  return next(request.clone({ setHeaders: { 'X-Correlation-ID': crypto.randomUUID() } }));
};
