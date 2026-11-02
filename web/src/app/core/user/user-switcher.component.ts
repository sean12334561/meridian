import { Component, inject } from '@angular/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { CurrentUserService } from './current-user.service';

@Component({
  selector: 'app-user-switcher',
  imports: [MatFormFieldModule, MatSelectModule],
  template: `
    <mat-form-field class="switcher" subscriptSizing="dynamic">
      <mat-label>Acting as</mat-label>
      <mat-select [value]="current.id()" (valueChange)="current.select($event)">
        @for (user of current.users(); track user.id) {
          <mat-option [value]="user.id">{{ user.name }} ({{ user.role }})</mat-option>
        }
      </mat-select>
    </mat-form-field>
  `,
  styles: `
    .switcher {
      width: 12rem;
      font-size: 0.85rem;
    }
  `,
})
export class UserSwitcherComponent {
  protected readonly current = inject(CurrentUserService);
}
