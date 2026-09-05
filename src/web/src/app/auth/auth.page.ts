import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService, authError, safeReturnUrl } from './auth.service';

/** Presents accessible login and registration forms within the StreamForge shell. */
@Component({
  selector: 'app-auth-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './auth.page.html',
  styleUrl: './auth.page.scss',
})
export class AuthPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly registering = this.route.snapshot.data['register'] === true;
  readonly returnUrl = safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
  readonly busy = signal(false);
  readonly error = signal('');
  readonly today = new Date().toISOString().slice(0, 10);
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    password: ['', [Validators.required, Validators.minLength(15), Validators.maxLength(128)]],
    username: [
      '',
      this.registering
        ? [
            Validators.required,
            Validators.minLength(3),
            Validators.maxLength(50),
            Validators.pattern(/^[\p{L}\p{Nd}_.-]+$/u),
          ]
        : [],
    ],
    confirmPassword: ['', this.registering ? [Validators.required] : []],
    dob: [''],
    address: ['', Validators.maxLength(1000)],
  });

  /** Submits validated credentials and follows only a safe local return URL. */
  async submit(): Promise<void> {
    if (this.busy()) return;
    this.error.set('');
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.error.set('Please check the highlighted fields.');
      return;
    }
    const value = this.form.getRawValue();
    if (this.registering && value.password !== value.confirmPassword) {
      this.error.set('Passwords do not match.');
      return;
    }
    if (this.registering && value.dob > this.today) {
      this.error.set('Date of birth cannot be in the future.');
      return;
    }
    this.busy.set(true);
    try {
      if (this.registering)
        await this.auth.register({
          username: value.username,
          email: value.email,
          password: value.password,
          ...(value.dob ? { dob: value.dob } : {}),
          ...(value.address ? { address: value.address } : {}),
        });
      else await this.auth.login(value.email, value.password);
      await this.router.navigateByUrl(this.returnUrl);
    } catch (error) {
      this.error.set(authError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
