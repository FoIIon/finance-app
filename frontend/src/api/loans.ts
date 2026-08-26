import apiClient from './client';
import type { Loan, LoanInstallment, DebtSummary, CreateLoan, UpdateLoan } from '../types/loan';

export const loansApi = {
  getAll: (dashboardId: number, includeArchived = false) =>
    apiClient.get<Loan[]>('/loans', { params: { dashboardId, includeArchived } }),

  getSummary: (dashboardId: number) =>
    apiClient.get<DebtSummary>('/loans/summary', { params: { dashboardId } }),

  /** Sans `months`, le tableau complet jusqu'à extinction. */
  getSchedule: (id: number, months?: number) =>
    apiClient.get<LoanInstallment[]>(`/loans/${id}/schedule`, { params: { months } }),

  create: (data: CreateLoan) => apiClient.post<Loan>('/loans', data),

  update: (id: number, data: UpdateLoan) => apiClient.put<Loan>(`/loans/${id}`, data),

  delete: (id: number) => apiClient.delete(`/loans/${id}`),
};
