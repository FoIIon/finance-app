import { useContext } from 'react';
import { DashboardContext } from '../context/dashboard-context';

export const useDashboards = () => useContext(DashboardContext);
