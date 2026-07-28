import apiClient from './client';
import type {
  Investment,
  InvestmentValuation,
  CreateInvestment,
  UpdateInvestment,
  CreateValuation,
} from '../types/investment';

export const investmentsApi = {
  getAll: (dashboardId: number) =>
    apiClient.get<Investment[]>('/investment', { params: { dashboardId } }),

  create: (data: CreateInvestment) =>
    apiClient.post<Investment>('/investment', data),

  update: (id: number, data: UpdateInvestment) =>
    apiClient.put<Investment>(`/investment/${id}`, data),

  delete: (id: number) =>
    apiClient.delete(`/investment/${id}`),

  addValuation: (id: number, data: CreateValuation) =>
    apiClient.post<Investment>(`/investment/${id}/valuation`, data),

  getValuations: (id: number) =>
    apiClient.get<InvestmentValuation[]>(`/investment/${id}/valuations`),
};
