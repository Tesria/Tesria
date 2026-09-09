import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import './index.css'
import { startFaviconSync } from './theme'
import { AuthProvider } from './auth/AuthContext'
import { ProtectedRoute } from './components/ProtectedRoute'
import { Layout } from './components/Layout'
import { ScrollToTop } from './components/ScrollToTop'
import { LoginPage } from './routes/LoginPage'
import { RegisterPage } from './routes/RegisterPage'
import { SpacesPage } from './routes/SpacesPage'
import { SpacePage } from './routes/SpacePage'
import { SpaceHome } from './routes/SpaceHome'
import { PageView } from './routes/PageView'
import { PageEditor } from './routes/PageEditor'
import { TrashPage } from './routes/TrashPage'
import { SearchPage } from './routes/SearchPage'
import { LabelPage } from './routes/LabelPage'
import { AuditPage } from './routes/AuditPage'
import { GroupsPage } from './routes/GroupsPage'
import { SpacePermissionsPage } from './routes/SpacePermissionsPage'
import { SpaceWebhooksPage } from './routes/SpaceWebhooksPage'
import { ApiTokensPage } from './routes/ApiTokensPage'
import { ProfilePage } from './routes/ProfilePage'
import { RecoverPage } from './routes/RecoverPage'
import { AdminLayout } from './routes/admin/AdminLayout'
import { AdminDashboardPage } from './routes/admin/AdminDashboardPage'
import { AdminUsersPage } from './routes/admin/AdminUsersPage'
import { AdminSpacesPage } from './routes/admin/AdminSpacesPage'
import { AdminInvitesPage } from './routes/admin/AdminInvitesPage'
import { AdminSettingsPage } from './routes/admin/AdminSettingsPage'
import { AdminSecurityPage } from './routes/admin/AdminSecurityPage'

// Paints the tab icon in the chosen accent before React renders, and keeps
// it in step when the OS flips light/dark.
startFaviconSync()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider>
      <BrowserRouter>
        <ScrollToTop />
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/recover" element={<RecoverPage />} />
          <Route path="/reset" element={<RecoverPage />} />
          <Route element={<ProtectedRoute />}>
            <Route element={<Layout />}>
              <Route index element={<Navigate to="/spaces" replace />} />
              <Route path="search" element={<SearchPage />} />
              <Route path="labels/:name" element={<LabelPage />} />
              <Route path="audit" element={<AuditPage />} />
              <Route path="groups" element={<GroupsPage />} />
              <Route path="api-tokens" element={<ApiTokensPage />} />
              <Route path="profile" element={<ProfilePage />} />
              <Route path="admin" element={<AdminLayout />}>
                <Route index element={<AdminDashboardPage />} />
                <Route path="users" element={<AdminUsersPage />} />
                <Route path="spaces" element={<AdminSpacesPage />} />
                <Route path="invites" element={<AdminInvitesPage />} />
                <Route path="security" element={<AdminSecurityPage />} />
                <Route path="settings" element={<AdminSettingsPage />} />
              </Route>
              <Route path="spaces" element={<SpacesPage />} />
              <Route path="spaces/:key" element={<SpacePage />}>
                <Route index element={<SpaceHome />} />
                <Route path="new" element={<PageEditor />} />
                <Route path="trash" element={<TrashPage />} />
                <Route path="permissions" element={<SpacePermissionsPage />} />
                <Route path="webhooks" element={<SpaceWebhooksPage />} />
                <Route path="pages/:pageId" element={<PageView />} />
                <Route path="pages/:pageId/edit" element={<PageEditor />} />
              </Route>
            </Route>
          </Route>
          <Route path="*" element={<Navigate to="/spaces" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  </StrictMode>,
)
