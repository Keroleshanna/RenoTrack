import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { I18n } from '../../core/i18n/i18n';
import { Auth } from '../../core/auth/auth';

/**
 * Sign-in.
 *
 * **Every failure renders the same message**, exactly as the API returns the same 401 for an unknown
 * email, a wrong password, an inactive account and a lockout alike. Distinguishing them here would
 * rebuild client-side the account-enumeration oracle the backend deliberately refuses to be
 * (CLAUDE.md §22) — the fact that a client *could* tell them apart from a richer response is
 * precisely why the response is not richer.
 *
 * **No role selector.** Role comes from the token's claim, which is the only place it could
 * legitimately come from.
 */
@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  template: `
    <div class="login">
      <form class="card" [formGroup]="form" (ngSubmit)="submit()">
        <div class="card__brand">
          <span class="card__mark" aria-hidden="true"></span>
          <span class="card__wordmark">{{ t().app.name }}</span>
        </div>

        <h1 class="rt-title">{{ t().login.title }}</h1>
        <p class="rt-body rt-muted card__subtitle">{{ t().login.subtitle }}</p>

        <label class="field">
          <span class="rt-label">{{ t().login.email }}</span>
          <input type="email" formControlName="email" autocomplete="username" />
          @if (form.controls.email.touched && form.controls.email.invalid) {
            <span class="field__error">{{ t().login.emailRequired }}</span>
          }
        </label>

        <label class="field">
          <span class="rt-label">{{ t().login.password }}</span>
          <input type="password" formControlName="password" autocomplete="current-password" />
          @if (form.controls.password.touched && form.controls.password.invalid) {
            <span class="field__error">{{ t().login.passwordRequired }}</span>
          }
        </label>

        @if (failed()) {
          <p class="card__error" role="alert">{{ t().login.failed }}</p>
        }

        <button type="submit" class="card__submit" [disabled]="pending()">
          {{ pending() ? t().login.signingIn : t().login.submit }}
        </button>
      </form>
    </div>
  `,
  styles: `
    .login {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      padding: var(--rt-space-6);
      background: var(--rt-surface-page);
    }
    .card {
      display: flex;
      flex-direction: column;
      width: 100%;
      max-width: 400px;
      padding: var(--rt-space-8);
      background: var(--rt-surface-raised);
      border-radius: var(--rt-radius-lg);
      box-shadow: var(--rt-shadow-3);
    }
    .card__brand {
      display: flex;
      align-items: center;
      gap: var(--rt-space-2);
      margin-bottom: var(--rt-space-6);
    }
    .card__mark {
      width: 15px;
      height: 15px;
      border-radius: 3px;
      background: var(--rt-accent);
    }
    .card__wordmark {
      font-size: 16px;
      font-weight: 700;
      letter-spacing: -0.01em;
    }
    .card__subtitle {
      margin: var(--rt-space-2) 0 var(--rt-space-6);
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: var(--rt-space-2);
      margin-bottom: var(--rt-space-4);
    }
    .field input {
      padding: var(--rt-space-3);
      border: 1px solid var(--rt-border-strong);
      border-radius: var(--rt-radius-md);
      font: inherit;
    }
    .field input:focus {
      outline: 2px solid var(--rt-brand);
      outline-offset: -1px;
      border-color: var(--rt-brand);
    }
    .field__error {
      color: var(--rt-danger);
      font-size: 12px;
    }
    .card__error {
      margin: 0 0 var(--rt-space-4);
      padding: var(--rt-space-3);
      background: var(--rt-status-lost-bg);
      border: 1px solid var(--rt-status-lost-border);
      border-radius: var(--rt-radius-md);
      color: var(--rt-status-lost-fg);
      font-size: 13px;
    }
    .card__submit {
      height: 44px;
      background: var(--rt-brand);
      border: 0;
      border-radius: var(--rt-radius-md);
      color: #fff;
      font: inherit;
      font-weight: 650;
      cursor: pointer;
    }
    .card__submit:hover:not(:disabled) {
      background: var(--rt-brand-strong);
    }
    .card__submit:disabled {
      opacity: 0.6;
      cursor: default;
    }
  `,
})
export class LoginPage {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly t = inject(I18n).t;

  protected readonly pending = signal(false);
  protected readonly failed = signal(false);

  protected readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected submit(): void {
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.pending.set(true);
    this.failed.set(false);

    const { email, password } = this.form.getRawValue();

    this.auth.login(email, password).subscribe({
      next: () => {
        this.pending.set(false);
        // `weiter` is set by the guard when a deep link was interrupted, so signing in resumes it
        // rather than always landing on the cockpit.
        const target = this.route.snapshot.queryParamMap.get('weiter') ?? '/cockpit';
        void this.router.navigateByUrl(target);
      },
      error: () => {
        this.pending.set(false);
        this.failed.set(true);
      },
    });
  }
}
