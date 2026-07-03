import apiClient from './client';
import type { Transaction, CreateTransaction, UpdateTransaction, TransactionSummary, TransactionFilters, CategoryMonthHistory, MonthlyReport } from '../types/transaction';

export const transactionsApi = {
  getAll: (filters?: TransactionFilters) =>
    apiClient.get<Transaction[]>('/transaction', { params: filters }),

  getById: (id: number) =>
    apiClient.get<Transaction>(`/transaction/${id}`),

  create: (data: CreateTransaction) =>
    apiClient.post<Transaction>('/transaction', data),

  update: (id: number, data: UpdateTransaction) =>
    apiClient.put<Transaction>(`/transaction/${id}`, data),

  delete: (id: number) =>
    apiClient.delete(`/transaction/${id}`),

  setExceptional: (id: number, isExceptional: boolean) =>
    apiClient.put<Transaction>(`/transaction/${id}/exceptional`, { isExceptional }),

  getSummary: (dashboardId?: number, from?: string, to?: string, bankAccountId?: number, includeExceptional?: boolean) =>
    apiClient.get<TransactionSummary>('/transaction/summary', {
      params: { dashboardId, from, to, bankAccountId, includeExceptional },
    }),

  getCategoryHistory: (dashboardId: number, categoryId: number, months = 12) =>
    apiClient.get<CategoryMonthHistory[]>('/transaction/category-history', {
      params: { dashboardId, categoryId, months },
    }),

  getMonthlyReport: (dashboardId: number | undefined, year: number, month: number) =>
    apiClient.get<MonthlyReport>('/transaction/monthly-report', {
      params: { dashboardId, year, month },
    }),

  recategorize: () =>
    apiClient.post<{ updated: number; total: number }>('/transaction/recategorize'),
};
