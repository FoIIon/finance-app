import { createContext, useState, useEffect, useCallback } from 'react';
import { dashboardsApi } from '../api/dashboards';
import { useAuth } from '../hooks/useAuth';
import type { Dashboard } from '../types/dashboard';

interface DashboardContextType {
  dashboards: Dashboard[];
  currentDashboard: Dashboard | null;
  setCurrentDashboard: (dashboard: Dashboard) => void;
  refreshDashboards: () => Promise<void>;
  loading: boolean;
}

export const DashboardContext = createContext<DashboardContextType>({
  dashboards: [],
  currentDashboard: null,
  setCurrentDashboard: () => {},
  refreshDashboards: async () => {},
  loading: false,
});

const STORAGE_KEY = 'finance-app:currentDashboardId';

export const DashboardProvider = ({ children }: { children: React.ReactNode }) => {
  const [dashboards, setDashboards] = useState<Dashboard[]>([]);
  const [currentDashboard, setCurrentDashboardState] = useState<Dashboard | null>(null);
  const [loading, setLoading] = useState(false);
  const { isAuthenticated } = useAuth();

  // Wrapper qui persiste la sélection
  const setCurrentDashboard = useCallback((dashboard: Dashboard) => {
    setCurrentDashboardState(dashboard);
    localStorage.setItem(STORAGE_KEY, String(dashboard.id));
  }, []);

  const refreshDashboards = useCallback(async () => {
    if (!isAuthenticated) return;
    setLoading(true);
    try {
      const response = await dashboardsApi.getAll();
      setDashboards(response.data);
      // Restaurer la sélection persistée si toujours valide, sinon premier dashboard
      const storedId = Number(localStorage.getItem(STORAGE_KEY));
      const stored = storedId ? response.data.find(d => d.id === storedId) : null;
      if (!currentDashboard || !response.data.find(d => d.id === currentDashboard.id)) {
        setCurrentDashboardState(stored ?? response.data[0] ?? null);
      }
    } catch {
      // Silently fail
    } finally {
      setLoading(false);
    }
  }, [isAuthenticated, currentDashboard]);

  useEffect(() => {
    refreshDashboards();
  }, [isAuthenticated]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <DashboardContext.Provider value={{ dashboards, currentDashboard, setCurrentDashboard, refreshDashboards, loading }}>
      {children}
    </DashboardContext.Provider>
  );
};
