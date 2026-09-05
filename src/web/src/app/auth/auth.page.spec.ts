import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { AuthPage } from './auth.page';
import { AuthService } from './auth.service';

describe('AuthPage', () => {
  it('shows a disabled forgot password action', async () => {
    TestBed.configureTestingModule({
      imports: [AuthPage],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: {} },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { data: {}, queryParamMap: { get: () => null } } },
        },
      ],
    });
    const fixture = TestBed.createComponent(AuthPage);
    fixture.detectChanges();
    const forgot = fixture.nativeElement.querySelector('.forgot button') as HTMLButtonElement;
    expect(forgot.disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Coming soon');
  });

  it('rejects mismatched registration passwords before calling the server', async () => {
    const register = vi.fn();
    TestBed.configureTestingModule({
      imports: [AuthPage],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { register } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { data: { register: true }, queryParamMap: { get: () => null } } },
        },
      ],
    });
    const fixture = TestBed.createComponent(AuthPage);
    fixture.componentInstance.form.patchValue({
      username: 'tester',
      email: 'test@example.test',
      password: 'a very long password',
      confirmPassword: 'different password',
    });
    await fixture.componentInstance.submit();
    expect(fixture.componentInstance.error()).toBe('Passwords do not match.');
    expect(register).not.toHaveBeenCalled();
  });
});
