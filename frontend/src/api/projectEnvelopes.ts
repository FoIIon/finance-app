import apiClient from './client';
import type {
  ProjectEnvelope,
  ProjectEnvelopeProgress,
  CreateProjectEnvelope,
  UpdateProjectEnvelope,
} from '../types/projectEnvelope';
import type { Transaction } from '../types/transaction';

export const projectEnvelopesApi = {
  getAll: (dashboardId: number) =>
    apiClient.get<ProjectEnvelopeProgress[]>('/projectenvelope', { params: { dashboardId } }),

  getTransactions: (id: number) =>
    apiClient.get<Transaction[]>(`/projectenvelope/${id}/transactions`),

  create: (data: CreateProjectEnvelope) =>
    apiClient.post<ProjectEnvelope>('/projectenvelope', data),

  update: (id: number, data: UpdateProjectEnvelope) =>
    apiClient.put<ProjectEnvelope>(`/projectenvelope/${id}`, data),

  delete: (id: number) =>
    apiClient.delete(`/projectenvelope/${id}`),
};
