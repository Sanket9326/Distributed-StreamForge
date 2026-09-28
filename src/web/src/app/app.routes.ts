import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

export const routes: Routes = [
  {
    path: 'watch-history',
    loadComponent: () => import('./history/watch-history.page').then((module) => module.WatchHistoryPage),
    canActivate: [authGuard],
    title: 'Watch history · StreamForge',
  },
  {
    path: 'subscriptions',
    loadComponent: () =>
      import('./subscriptions/subscriptions.page').then((module) => module.SubscriptionsPage),
    canActivate: [authGuard],
    title: 'Subscriptions · StreamForge',
  },
  {
    path: '',
    loadComponent: () => import('./feed/home-feed.page').then((module) => module.HomeFeedPage),
    title: 'Home · StreamForge',
  },
  {
    path: 'watch/:videoId',
    loadComponent: () => import('./feed/watch.page').then((module) => module.WatchPage),
    title: 'Watch · StreamForge',
  },
  {
    path: 'upload',
    loadComponent: () => import('./upload/upload.page').then((module) => module.UploadPage),
    canActivate: [authGuard],
    title: 'Upload · StreamForge',
  },
  {
    path: 'login',
    loadComponent: () => import('./auth/auth.page').then((module) => module.AuthPage),
    title: 'Log in · StreamForge',
  },
  {
    path: 'register',
    loadComponent: () => import('./auth/auth.page').then((module) => module.AuthPage),
    data: { register: true },
    title: 'Create account · StreamForge',
  },
  { path: '**', redirectTo: '' },
];
