import { Suspense, lazy } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { DashboardProvider } from './context/DashboardContext';
import { PeriodProvider } from './context/PeriodContext';
import { ToastProvider } from './context/ToastContext';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';
import Login from './pages/Login';
import Register from './pages/Register';
import RegisterSuccess from './pages/RegisterSuccess';
import ConfirmEmail from './pages/ConfirmEmail';
import Dashboard from './pages/Dashboard';
import AcceptInvitation from './pages/AcceptInvitation';

const DashboardOverview = lazy(() => import('./pages/dashboard/Overview'));
const DashboardBilan = lazy(() => import('./pages/dashboard/Bilan'));
const DashboardCategories = lazy(() => import('./pages/dashboard/Categories'));
const DashboardProjects = lazy(() => import('./pages/dashboard/Projects'));
const DashboardTriage = lazy(() => import('./pages/dashboard/Triage'));
const Transactions = lazy(() => import('./pages/Transactions'));
const Categories = lazy(() => import('./pages/Categories'));
const Budgets = lazy(() => import('./pages/Budgets'));
const ExceptionalExpenses = lazy(() => import('./pages/ExceptionalExpenses'));
const Bank = lazy(() => import('./pages/Bank'));
const DashboardSettings = lazy(() => import('./pages/DashboardSettings'));
const RecurringTransactions = lazy(() => import('./pages/RecurringTransactions'));

const RouteFallback = () => (
  <div className="flex items-center justify-center h-64 animate-[fadeIn_0.5s_ease-out]">
    <div className="text-white/40">Chargement…</div>
  </div>
);

const App = () => {
  return (
    <ToastProvider>
    <AuthProvider>
      <DashboardProvider>
        <PeriodProvider>
          <BrowserRouter>
            <Suspense fallback={<RouteFallback />}>
              <Routes>
                <Route path="/login" element={<Login />} />
                <Route path="/register" element={<Register />} />
                <Route path="/register-success" element={<RegisterSuccess />} />
                <Route path="/confirm-email" element={<ConfirmEmail />} />
                <Route path="/invitation/accept" element={<AcceptInvitation />} />
                <Route
                  element={
                    <ProtectedRoute>
                      <Layout />
                    </ProtectedRoute>
                  }
                >
                  <Route path="/" element={<Navigate to="/dashboard/overview" replace />} />
                  <Route path="/dashboard" element={<Dashboard />}>
                    <Route index element={<Navigate to="overview" replace />} />
                    <Route path="overview" element={<DashboardOverview />} />
                    <Route path="bilan" element={<DashboardBilan />} />
                    <Route path="categories" element={<DashboardCategories />} />
                    <Route path="projects" element={<DashboardProjects />} />
                    <Route path="triage" element={<DashboardTriage />} />
                  </Route>
                  <Route path="/transactions" element={<Transactions />} />
                  <Route path="/categories" element={<Categories />} />
                  <Route path="/budgets" element={<Budgets />} />
                  <Route path="/exceptional" element={<ExceptionalExpenses />} />
                  <Route path="/bank" element={<Bank />} />
                  <Route path="/dashboard-settings" element={<DashboardSettings />} />
                  <Route path="/recurring" element={<RecurringTransactions />} />
                </Route>
                <Route path="*" element={<Navigate to="/dashboard/overview" replace />} />
              </Routes>
            </Suspense>
          </BrowserRouter>
        </PeriodProvider>
      </DashboardProvider>
    </AuthProvider>
    </ToastProvider>
  );
};

export default App;
