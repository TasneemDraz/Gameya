import { Routes } from '@angular/router';
import { AppLayout } from './layout/app-layout';

// Feature routes are added by later tasks; PublicLayout wraps the logged-out ones.
export const routes: Routes = [{ path: '', component: AppLayout, children: [] }];
