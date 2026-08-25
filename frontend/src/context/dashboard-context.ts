import { createContext } from 'react';
import type { Dashboard } from '../types/dashboard';

export interface DashboardContextType {
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
