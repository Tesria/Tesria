import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter, createRoutesFromElements, Navigate, Route, RouterProvider } from 'react-router-dom'
import './index.css'
import { startFaviconSync } from './theme'
import { AuthProvider } from './auth/AuthContext'
import { InstanceProvider } from './InstanceContext'
import { ProtectedRoute } from './components/ProtectedRoute'
import { SessionGate } from './components/SessionGate'
import { Layout } from './components/Layout'
import { Root } from './Root'
import { LoginPage } from './routes/LoginPage'
import { SetupPage } from './routes/SetupPage'
import { WelcomePage } from './routes/WelcomePage'
import { ExportPage } from './routes/ExportPage'
import { RegisterPage } from './routes/RegisterPage'
import { SpacesPage } from './routes/SpacesPage'
import { SpacePage } from './routes/SpacePage'
import { SpaceSettingsLayout } from './routes/SpaceSettingsLayout'
import { SpaceSettingsPage } from './routes/SpaceSettingsPage'
import { SpaceHome } from './routes/SpaceHome'
import { PageView } from './routes/PageView'
import { PageEditor } from './routes/PageEditor'
import { TrashPage } from './routes/TrashPage'
import { SearchPage } from './routes/SearchPage'
import { LabelPage } from './routes/LabelPage'
import { LabelsIndexPage } from './routes/LabelsIndexPage'
import { AuditPage } from './routes/AuditPage'
import { GroupsPage } from './routes/GroupsPage'
import { SpacePermissionsPage } from './routes/SpacePermissionsPage'
import { SpaceTemplatesPage } from './routes/SpaceTemplatesPage'
import { SpaceWebhooksPage } from './routes/SpaceWebhooksPage'
import { ProfilePage } from './routes/ProfilePage'
import { RecoverPage } from './routes/RecoverPage'
import { AdminLayout } from './routes/admin/AdminLayout'
import { AdminDashboardPage } from './routes/admin/AdminDashboardPage'
import { AdminUsersPage } from './routes/admin/AdminUsersPage'
import { AdminSpacesPage } from './routes/admin/AdminSpacesPage'
import { AdminInvitesPage } from './routes/admin/AdminInvitesPage'
import { InvitePage } from './routes/InvitePage'
import { AdminSettingsPage } from './routes/admin/AdminSettingsPage'
import { AdminSecurityPage } from './routes/admin/AdminSecurityPage'
import { AdminApiTokensPage } from './routes/admin/AdminApiTokensPage'
import { AdminAboutPage } from './routes/admin/AdminAboutPage'
import { AdminBackupsPage } from './routes/admin/AdminBackupsPage'
import { AdminRolesPage } from './routes/admin/AdminRolesPage'
import { AdminBrandingPage } from './routes/admin/AdminBrandingPage'

// Paints the tab icon in the chosen accent before React renders, and keeps
// it in step when the OS flips light/dark.
startFaviconSync()

// A data router (createBrowserRouter), not <BrowserRouter>: only a data
// router supports useBlocker, which is what lets the editor ask before a
// navigation leaves it, including the browser's back button and an
// iPhone's swipe-back, which no click handler can see.
const router = createBrowserRouter(
  createRoutesFromElements(
        <Route element={<Root />}>
          {/* First-run setup (dev-plan 10.2). Outside SessionGate: on a
              brand-new instance there is no session to gate on. */}
          <Route path="/setup" element={<SetupPage />} />
          {/* The welcome tour (dev-plan 10.3). Signed in, but outside the
              shell: it is full-screen and has no use for a topbar. */}
          <Route path="/welcome" element={<WelcomePage />} />
          {/* What an export is captured from (dev-plan 12.1). Outside the
              shell and outside every gate: a redirect here would be captured
              instead of the page. */}
          <Route path="/export/pages/:id" element={<ExportPage />} />
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/recover" element={<RecoverPage />} />
          <Route path="/reset" element={<RecoverPage />} />
          {/* The shell renders for anonymous readers too (dev-plan 5.3);
              the server decides what they can see. Routes that need an
              account sit under a nested ProtectedRoute. */}
          <Route element={<SessionGate />}>
            <Route element={<Layout />}>
              <Route index element={<Navigate to="/spaces" replace />} />
              <Route path="search" element={<SearchPage />} />
              <Route path="spaces" element={<SpacesPage />} />
              <Route path="spaces/:key" element={<SpacePage />}>
                <Route index element={<SpaceHome />} />
                <Route path="pages/:pageId" element={<PageView />} />
                <Route element={<ProtectedRoute />}>
                  {/* A space's own administration, one section with four
                      tabs. Permissions, webhooks and trash used to be
                      siblings of settings because they were built before it
                      existed; they are tabs of it now. */}
                  <Route path="settings" element={<SpaceSettingsLayout />}>
                    <Route index element={<SpaceSettingsPage />} />
                    <Route path="permissions" element={<SpacePermissionsPage />} />
                    <Route path="templates" element={<SpaceTemplatesPage />} />
                    <Route path="webhooks" element={<SpaceWebhooksPage />} />
                    <Route path="trash" element={<TrashPage />} />
                  </Route>
                  <Route path="new" element={<PageEditor />} />
                  <Route path="pages/:pageId/edit" element={<PageEditor />} />
                  {/* Old locations, kept for bookmarks and for links written
                      into pages before the move. */}
                  <Route path="trash" element={<Navigate to="../settings/trash" relative="path" replace />} />
                  <Route path="permissions" element={<Navigate to="../settings/permissions" relative="path" replace />} />
                  <Route path="webhooks" element={<Navigate to="../settings/webhooks" relative="path" replace />} />
                </Route>
              </Route>
              <Route element={<ProtectedRoute />}>
                <Route path="labels" element={<LabelsIndexPage />} />
                <Route path="labels/:name" element={<LabelPage />} />
                {/* Old locations, kept for bookmarks. */}
                <Route path="audit" element={<Navigate to="/admin/audit" replace />} />
                <Route path="groups" element={<Navigate to="/admin/groups" replace />} />
                <Route path="api-tokens" element={<Navigate to="/profile#api-tokens" replace />} />
                <Route path="profile" element={<ProfilePage />} />
                <Route path="invite" element={<InvitePage />} />
                <Route path="admin" element={<AdminLayout />}>
                  <Route index element={<AdminDashboardPage />} />
                  <Route path="users" element={<AdminUsersPage />} />
                  <Route path="spaces" element={<AdminSpacesPage />} />
                  <Route path="invites" element={<AdminInvitesPage />} />
                  <Route path="api-tokens" element={<AdminApiTokensPage />} />
                  <Route path="security" element={<AdminSecurityPage />} />
                  <Route path="backups" element={<AdminBackupsPage />} />
                  <Route path="roles" element={<AdminRolesPage />} />
                  <Route path="groups" element={<GroupsPage />} />
                  <Route path="audit" element={<AuditPage />} />
                  <Route path="settings" element={<AdminSettingsPage />} />
                  <Route path="branding" element={<AdminBrandingPage />} />
                  <Route path="about" element={<AdminAboutPage />} />
                </Route>
              </Route>
            </Route>
          </Route>
          <Route path="*" element={<Navigate to="/spaces" replace />} />
        </Route>,
  ),
)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <InstanceProvider>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </InstanceProvider>
  </StrictMode>,
)
