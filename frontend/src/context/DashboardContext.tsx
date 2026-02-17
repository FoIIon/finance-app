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

export const DashboardProvider = ({ children }: { children: React.ReactNode }) => {
  const [dashboards, setDashboards] = useState<Dashboard[]>([]);
  const [currentDashboard, setCurrentDashboard] = useState<Dashboard | null>(null);
  const [loading, setLoading] = useState(false);
  const { isAuthenticated } = useAuth();

  const refreshDashboards = useCallback(async () => {
    if (!isAuthenticated) return;
    setLoading(true);
    try {
      const response = await dashboardsApi.getAll();
      setDashboards(response.data);
      // Sélectionner le premier dashboard si aucun n'est sélectionné
      if (!currentDashboard || !response.data.find(d => d.id === currentDashboard.id)) {
        setCurrentDashboard(response.data[0] ?? null);
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
