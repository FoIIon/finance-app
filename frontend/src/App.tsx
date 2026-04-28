import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { DashboardProvider } from './context/DashboardContext';
import { ToastProvider } from './context/ToastContext';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';
import Login from './pages/Login';
import Register from './pages/Register';
import RegisterSuccess from './pages/RegisterSuccess';
import ConfirmEmail from './pages/ConfirmEmail';
import Dashboard from './pages/Dashboard';
import Transactions from './pages/Transactions';
import Categories from './pages/Categories';
import Bank from './pages/Bank';
import DashboardSettings from './pages/DashboardSettings';
import AcceptInvitation from './pages/AcceptInvitation';
import RecurringTransactions from './pages/RecurringTransactions';

const App = () => {
  return (
    <ToastProvider>
    <AuthProvider>
      <DashboardProvider>
        <BrowserRouter>
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
              <Route path="/" element={<Dashboard />} />
              <Route path="/transactions" element={<Transactions />} />
              <Route path="/categories" element={<Categories />} />
              <Route path="/bank" element={<Bank />} />
              <Route path="/dashboard-settings" element={<DashboardSettings />} />
              <Route path="/recurring" element={<RecurringTransactions />} />
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </DashboardProvider>
    </AuthProvider>
    </ToastProvider>
  );
};

export default App;
