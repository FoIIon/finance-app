import apiClient from './client';
import type { Budget, BudgetProgress, CreateBudget, UpdateBudget } from '../types/budget';

export const budgetsApi = {
  getAll: (dashboardId: number) =>
    apiClient.get<Budget[]>('/budgets', { params: { dashboardId } }),

  getProgress: (dashboardId: number, from?: string, to?: string) =>
    apiClient.get<BudgetProgress[]>('/budgets/progress', { params: { dashboardId, from, to } }),

  create: (data: CreateBudget) =>
    apiClient.post<Budget>('/budgets', data),

  update: (id: number, data: UpdateBudget) =>
    apiClient.put<Budget>(`/budgets/${id}`, data),

  delete: (id: number) =>
    apiClient.delete(`/budgets/${id}`),
};
