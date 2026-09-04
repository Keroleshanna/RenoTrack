import { Routes } from '@angular/router';

import { authGuard, roleGuard } from './core/auth/auth-guard';

/**
 * The application's routes.
 *
 * **`/cockpit` is the landing page.** The Lead pipeline is one workspace among several, not the
 * Dashboard.
 *
 * German path segments throughout, matching the product's primary language. The English UI
 * translates labels, never URLs — a URL is an address, not a string a user reads (D79).
 *
 * **Guards are presentation, never a security boundary** (CLAUDE.md §23). Every endpoint behind
 * these screens carries its own `[Authorize]`; deleting this file would make the app ruder, not less
 * secure. `/rechnungen` carries a role guard because `PermissionMatrix.md` §5 makes every Invoice
 * action Admin-only, so sending an Inspector there would only earn them a 403.
 */
export const routes: Routes = [
  {
    path: 'anmelden',
    loadComponent: () => import('./features/login/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'cockpit',
    canActivate: [authGuard],
    loadComponent: () => import('./features/cockpit/cockpit-page').then((m) => m.CockpitPage),
  },
  {
    path: 'leads',
    canActivate: [authGuard],
    loadComponent: () => import('./features/workspaces/leads-page').then((m) => m.LeadsPage),
  },
  {
    path: 'leads/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/leads/lead-detail-page').then((m) => m.LeadDetailPage),
  },
  {
    path: 'besichtigungen',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/workspaces/inspections-page').then((m) => m.InspectionsPage),
  },
  {
    // Wireframe C3, the on-site screen. Both roles may open it (§2 gives Admin "F" on viewing an
    // Inspection); only the assigned Inspector gets the controls, and only until BR-10 closes it.
    path: 'besichtigungen/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/besichtigungen/inspection-detail-page').then(
        (m) => m.InspectionDetailPage,
      ),
  },
  {
    path: 'angebote',
    canActivate: [authGuard],
    loadComponent: () => import('./features/workspaces/angebote-page').then((m) => m.AngebotePage),
  },
  {
    // The builder (Wireframe D1) and the review screen (D3) are one route: D3 is D1's read view,
    // and what differs is capability, not layout. See `angebot-capabilities.ts`.
    path: 'angebote/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/angebote/angebot-detail-page').then((m) => m.AngebotDetailPage),
  },
  {
    path: 'rechnungen',
    canActivate: [roleGuard('admin')],
    loadComponent: () => import('./features/workspaces/invoices-page').then((m) => m.InvoicesPage),
  },
  {
    path: 'projekte',
    canActivate: [authGuard],
    loadComponent: () => import('./features/workspaces/projects-page').then((m) => m.ProjectsPage),
  },
  {
    // Readable by both roles (§5 grants Inspector "R"), so no role guard — the Invoice *actions* on
    // it are Admin-only, and each endpoint enforces that itself.
    path: 'projekte/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/projekte/project-detail-page').then((m) => m.ProjectDetailPage),
  },
  {
    // Wireframe F1. Both roles read the Catalog (§6 grants View F/F), so no role guard — the
    // management controls inside are Admin-only, and each endpoint enforces that itself.
    path: 'katalog',
    canActivate: [authGuard],
    loadComponent: () => import('./features/catalog/catalog-page').then((m) => m.CatalogPage),
  },
  {
    // `PermissionMatrix.md` §9 gives an Inspector no access to either action, so the role guard
    // spares them a screen that would 403 on every request.
    path: 'benachrichtigungen',
    canActivate: [roleGuard('admin')],
    loadComponent: () =>
      import('./features/notifications/notifications-page').then((m) => m.NotificationsPage),
  },
  { path: '', pathMatch: 'full', redirectTo: 'cockpit' },
  { path: '**', redirectTo: 'cockpit' },
];
