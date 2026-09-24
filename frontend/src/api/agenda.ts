import apiClient from './client';
import type { AgendaItem, AgendaResult, AgendaView, LinkRecurring, RecurringCandidate } from '../types/agenda';

export const agendaApi = {
  /** anchor absent : le serveur prend aujourd'hui dans le fuseau du ménage. */
  get: (dashboardId: number, view: AgendaView, anchor?: string) =>
    apiClient.get<AgendaResult>('/agenda', {
      params: { dashboardId, view, ...(anchor ? { anchor } : {}) },
    }),

  /** Les transactions d'un mois (yyyy-MM) qu'on peut désigner comme règlement de la récurrente. */
  recurringCandidates: (recurringId: number, dashboardId: number, month: string) =>
    apiClient.get<RecurringCandidate[]>(`/agenda/recurring/${recurringId}/candidates`, { params: { dashboardId, month } }),

  /** « C'est celle-ci » : pose le lien, rend l'item d'Agenda recalculé (null si la récurrente n'a pas d'occurrence ce mois-là). 409 si la transaction règle déjà autre chose. */
  linkRecurring: (recurringId: number, data: LinkRecurring) =>
    apiClient.post<AgendaItem | null>(`/agenda/recurring/${recurringId}/link`, data),

  /** « Ce n'est pas celle-ci » : retire le lien. 404 s'il ne valait pas cette récurrente. */
  unlinkRecurring: (recurringId: number, dashboardId: number, transactionId: number) =>
    apiClient.delete<void>(`/agenda/recurring/${recurringId}/link`, { params: { dashboardId, transactionId } }),
};
