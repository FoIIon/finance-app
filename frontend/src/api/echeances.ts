import apiClient from './client';
import { echeancesApi as agendaEcheancesApi } from './agenda';
import type { Echeance } from '../types/agenda';
import type { CreateEcheance } from '../types/echeance';

/** Les gestes du lot 2 (lecture, payer, dépayer, remplacer) plus ceux du lot 1 : lister, créer, supprimer. */
export const echeancesApi = {
  ...agendaEcheancesApi,

  getAll: (dashboardId: number) => apiClient.get<Echeance[]>('/echeances', { params: { dashboardId } }),

  create: (data: CreateEcheance) => apiClient.post<Echeance>('/echeances', data),

  /** Les documents rattachés restent, détachés (FK en SetNull côté serveur). */
  remove: (id: number) => apiClient.delete<void>(`/echeances/${id}`),
};
