import apiClient from './client';
import type { Echeance, UpdateEcheance } from '../types/agenda';
import type { CreateEcheance } from '../types/echeance';

/** L'unique client des échéances : lecture, payer, dépayer, remplacer (lot 2), lister, créer, supprimer (lot 1). */
export const echeancesApi = {
  getAll: (dashboardId: number) => apiClient.get<Echeance[]>('/echeances', { params: { dashboardId } }),

  getById: (id: number) => apiClient.get<Echeance>(`/echeances/${id}`),

  create: (data: CreateEcheance) => apiClient.post<Echeance>('/echeances', data),

  /** Remplacement complet des champs saisis : on renvoie tous les champs lus, seuls ceux qui changent diffèrent. Ne touche pas au lien de paiement. */
  update: (id: number, data: UpdateEcheance) => apiClient.put<Echeance>(`/echeances/${id}`, data),

  pay: (id: number) => apiClient.post<Echeance>(`/echeances/${id}/pay`),

  /** « Finalement non » : défait le paiement, manuel ou prouvé par transaction. Seul chemin pour détacher une transaction. */
  unpay: (id: number) => apiClient.post<Echeance>(`/echeances/${id}/unpay`),

  /** Les documents rattachés restent, détachés (FK en SetNull côté serveur). */
  remove: (id: number) => apiClient.delete<void>(`/echeances/${id}`),
};
