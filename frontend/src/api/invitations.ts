import apiClient from './client';
import type { Invitation } from '../types/dashboard';
import type { MessageResponse } from '../types/auth';

export const invitationsApi = {
  send: (dashboardId: number, email: string) =>
    apiClient.post<Invitation>('/invitation/send', { dashboardId, email }),

  accept: (token: string) =>
    apiClient.post<MessageResponse>('/invitation/accept', { token }),

  getPending: (dashboardId: number) =>
    apiClient.get<Invitation[]>(`/invitation/pending/${dashboardId}`),

  cancel: (id: number) =>
    apiClient.delete(`/invitation/${id}`),
};
