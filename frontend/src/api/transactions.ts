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

  getSummary: () =>
    apiClient.get<TransactionSummary>('/transaction/summary'),
};
