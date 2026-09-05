import { Routes } from '@angular/router';
import { HomeFeedPage } from './feed/home-feed.page';
import { UploadPage } from './upload/upload.page';
import { AuthPage } from './auth/auth.page';
import { authGuard } from './auth/auth.guard';

export const routes: Routes = [
  { path: '', component: HomeFeedPage, title: 'Home · StreamForge' },
  {
    path: 'upload',
    component: UploadPage,
    canActivate: [authGuard],
    title: 'Upload · StreamForge',
  },
  { path: 'login', component: AuthPage, title: 'Log in · StreamForge' },
  {
    path: 'register',
    component: AuthPage,
    data: { register: true },
    title: 'Create account · StreamForge',
  },
  { path: '**', redirectTo: '' },
];
