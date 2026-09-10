import apiClient from './client';
import type { AgendaResult, AgendaView } from '../types/agenda';

export const agendaApi = {
  /** anchor absent : le serveur prend aujourd'hui dans le fuseau du ménage. */
  get: (dashboardId: number, view: AgendaView, anchor?: string) =>
    apiClient.get<AgendaResult>('/agenda', {
      params: { dashboardId, view, ...(anchor ? { anchor } : {}) },
    }),
};
