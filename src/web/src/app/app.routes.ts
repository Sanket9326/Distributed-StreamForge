import { Routes } from '@angular/router';
import { HomeFeedPage } from './feed/home-feed.page';
import { UploadPage } from './upload/upload.page';

export const routes: Routes = [
  { path: '', component: HomeFeedPage, title: 'Home · StreamForge' },
  { path: 'upload', component: UploadPage, title: 'Upload · StreamForge' },
  { path: '**', redirectTo: '' },
];
