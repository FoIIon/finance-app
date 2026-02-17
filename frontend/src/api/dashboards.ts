import apiClient from './client';
import type { Dashboard, DashboardDetail, CreateDashboard } from '../types/dashboard';

export const dashboardsApi = {
  getAll: () =>
    apiClient.get<Dashboard[]>('/dashboard'),

  getById: (id: number) =>
    apiClient.get<DashboardDetail>(`/dashboard/${id}`),

  create: (data: CreateDashboard) =>
    apiClient.post<Dashboard>('/dashboard', data),

  update: (id: number, data: { name: string }) =>
    apiClient.put<Dashboard>(`/dashboard/${id}`, data),

  delete: (id: number) =>
    apiClient.delete(`/dashboard/${id}`),

  addAccount: (dashboardId: number, accountId: number) =>
    apiClient.post(`/dashboard/${dashboardId}/accounts`, { accountId }),

  removeAccount: (dashboardId: number, accountId: number) =>
    apiClient.delete(`/dashboard/${dashboardId}/accounts/${accountId}`),
};
