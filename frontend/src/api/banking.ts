import apiClient from './client';
import type { Institution, BankConnection, BankAccount, CategoryRule, CreateCategoryRule, UpdateCategoryRule } from '../types/banking';

export const bankingApi = {
  getInstitutions: (country: string) =>
    apiClient.get<Institution[]>('/banking/institutions', { params: { country } }),

  connect: (data: { institutionId: string; institutionName: string; institutionLogo: string }) =>
    apiClient.post<{ authorizationUrl: string }>('/banking/connect', data),

  callback: (ref: string) =>
    apiClient.get('/banking/callback', { params: { ref } }),

  getConnections: () =>
    apiClient.get<BankConnection[]>('/banking/connections'),

  deleteConnection: (id: number) =>
    apiClient.delete(`/banking/connections/${id}`),

  syncConnection: (id: number) =>
    apiClient.post(`/banking/connections/${id}/sync`),

  getAccounts: () =>
    apiClient.get<BankAccount[]>('/banking/accounts'),

  updateAccount: (id: number, data: { isActive: boolean }) =>
    apiClient.patch(`/banking/accounts/${id}`, data),
};

export const categoryRulesApi = {
  getAll: () =>
    apiClient.get<CategoryRule[]>('/categoryrules'),

  create: (data: CreateCategoryRule) =>
    apiClient.post<CategoryRule>('/categoryrules', data),

  update: (id: number, data: UpdateCategoryRule) =>
    apiClient.put<CategoryRule>(`/categoryrules/${id}`, data),

  delete: (id: number) =>
    apiClient.delete(`/categoryrules/${id}`),
};
