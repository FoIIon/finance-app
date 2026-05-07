import apiClient from './client';
import type { Transaction, CreateTransaction, UpdateTransaction, TransactionSummary, TransactionFilters } from '../types/transaction';

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

  getSummary: (dashboardId?: number, from?: string, to?: string, bankAccountId?: number) =>
    apiClient.get<TransactionSummary>('/transaction/summary', {
      params: { dashboardId, from, to, bankAccountId },
    }),

  recategorize: () =>
    apiClient.post<{ updated: number; total: number }>('/transaction/recategorize'),
};
