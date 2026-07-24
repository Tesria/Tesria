import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import './index.css'
import { AuthProvider } from './auth/AuthContext'
import { ProtectedRoute } from './components/ProtectedRoute'
import { Layout } from './components/Layout'
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

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route element={<ProtectedRoute />}>
            <Route element={<Layout />}>
              <Route index element={<Navigate to="/spaces" replace />} />
              <Route path="search" element={<SearchPage />} />
              <Route path="labels/:name" element={<LabelPage />} />
              <Route path="audit" element={<AuditPage />} />
              <Route path="groups" element={<GroupsPage />} />
              <Route path="spaces" element={<SpacesPage />} />
              <Route path="spaces/:key" element={<SpacePage />}>
                <Route index element={<SpaceHome />} />
                <Route path="new" element={<PageEditor />} />
                <Route path="trash" element={<TrashPage />} />
                <Route path="permissions" element={<SpacePermissionsPage />} />
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
