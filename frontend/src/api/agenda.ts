import apiClient from './client';
import type { AgendaResult, AgendaView, Echeance, UpdateEcheance } from '../types/agenda';

export const agendaApi = {
  /** anchor absent : le serveur prend aujourd'hui dans le fuseau du ménage. */
  get: (dashboardId: number, view: AgendaView, anchor?: string) =>
    apiClient.get<AgendaResult>('/agenda', {
      params: { dashboardId, view, ...(anchor ? { anchor } : {}) },
    }),
};

export const echeancesApi = {
  getById: (id: number) => apiClient.get<Echeance>(`/echeances/${id}`),

  pay: (id: number) => apiClient.post<Echeance>(`/echeances/${id}/pay`),

  unpay: (id: number) => apiClient.post<Echeance>(`/echeances/${id}/unpay`),

  /** Remplacement complet : on renvoie tous les champs lus, seul transactionId change. */
  update: (id: number, data: UpdateEcheance) => apiClient.put<Echeance>(`/echeances/${id}`, data),
};
