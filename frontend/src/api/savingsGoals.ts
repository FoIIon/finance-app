import apiClient from './client';
import type { SavingsGoal, SavingsGoalProgress, CreateSavingsGoal, UpdateSavingsGoal, AccountBalance } from '../types/savingsGoal';
import type { Transaction } from '../types/transaction';

export const savingsGoalsApi = {
  getAll: (dashboardId: number) =>
    apiClient.get<SavingsGoal[]>('/savings-goals', { params: { dashboardId } }),

  getProgress: (dashboardId: number) =>
    apiClient.get<SavingsGoalProgress[]>('/savings-goals/progress', { params: { dashboardId } }),

  create: (data: CreateSavingsGoal) =>
    apiClient.post<SavingsGoal>('/savings-goals', data),

  update: (id: number, data: UpdateSavingsGoal) =>
    apiClient.put<SavingsGoal>(`/savings-goals/${id}`, data),

  delete: (id: number) =>
    apiClient.delete(`/savings-goals/${id}`),
};

export const dashboardExtrasApi = {
  getAccountBalances: (dashboardId: number, to?: string) =>
    apiClient.get<AccountBalance[]>('/transaction/account-balances', { params: { dashboardId, to } }),

  refreshBalances: (dashboardId: number) =>
    apiClient.post<AccountBalance[]>('/transaction/refresh-balances', null, { params: { dashboardId } }),

  getRecentTransactions: (dashboardId: number, limit = 5, bankAccountId?: number) =>
    apiClient.get<Transaction[]>('/transaction/recent', { params: { dashboardId, limit, bankAccountId } }),

  getUncategorized: (dashboardId: number, limit = 50, bankAccountId?: number) =>
    apiClient.get<Transaction[]>('/transaction/uncategorized', { params: { dashboardId, limit, bankAccountId } }),

  getAnomalies: (dashboardId: number, from?: string, to?: string) =>
    apiClient.get<Transaction[]>('/transaction/anomalies', { params: { dashboardId, from, to } }),
};
