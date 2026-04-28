import apiClient from './client';
import type {
  RecurringTransaction,
  CreateRecurringTransaction,
  UpdateRecurringTransaction,
} from '../types/recurringTransaction';

export const recurringTransactionsApi = {
  getAll: (dashboardId: number) =>
    apiClient.get<RecurringTransaction[]>(`/dashboards/${dashboardId}/recurring`),

  getById: (dashboardId: number, id: number) =>
    apiClient.get<RecurringTransaction>(`/dashboards/${dashboardId}/recurring/${id}`),

  create: (dashboardId: number, data: CreateRecurringTransaction) =>
    apiClient.post<RecurringTransaction>(`/dashboards/${dashboardId}/recurring`, data),

  update: (dashboardId: number, id: number, data: UpdateRecurringTransaction) =>
    apiClient.put<RecurringTransaction>(`/dashboards/${dashboardId}/recurring/${id}`, data),

  delete: (dashboardId: number, id: number) =>
    apiClient.delete(`/dashboards/${dashboardId}/recurring/${id}`),
};
