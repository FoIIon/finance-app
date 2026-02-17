import apiClient from './client';
import type { Account, CreateAccount } from '../types/dashboard';

export const accountsApi = {
  getAll: () =>
    apiClient.get<Account[]>('/account'),

  create: (data: CreateAccount) =>
    apiClient.post<Account>('/account', data),

  update: (id: number, data: { name: string }) =>
    apiClient.put<Account>(`/account/${id}`, data),

  delete: (id: number) =>
    apiClient.delete(`/account/${id}`),
};
