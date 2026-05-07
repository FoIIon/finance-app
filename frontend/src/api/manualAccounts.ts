import apiClient from './client';
import type { ManualAccount, CreateManualAccount, UpdateManualAccount } from '../types/savingsGoal';

export const manualAccountsApi = {
  getAll: () => apiClient.get<ManualAccount[]>('/banking/manual-accounts'),
  create: (data: CreateManualAccount) => apiClient.post<ManualAccount>('/banking/manual-accounts', data),
  update: (id: number, data: UpdateManualAccount) => apiClient.put<ManualAccount>(`/banking/manual-accounts/${id}`, data),
  delete: (id: number) => apiClient.delete(`/banking/manual-accounts/${id}`),
};
