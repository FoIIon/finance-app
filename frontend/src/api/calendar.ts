import apiClient from './client';
import type { CalendarStatus } from '../types/agenda';

// L'adresse ICS ne transite qu'ici, dans le corps du PUT. Elle ne revient jamais du serveur,
// elle n'est jamais mise en cache ni loguée.
export const calendarApi = {
  getSource: (dashboardId: number) =>
    apiClient.get<CalendarStatus>('/calendar/source', { params: { dashboardId } }),

  /** Synchronise dans la requête, jusqu'à 30 s : pas de timeout court ici. */
  putSource: (dashboardId: number, url: string) =>
    apiClient.put<CalendarStatus>('/calendar/source', { url }, { params: { dashboardId } }),

  deleteSource: (dashboardId: number) =>
    apiClient.delete('/calendar/source', { params: { dashboardId } }),

  refresh: (dashboardId: number) =>
    apiClient.post<CalendarStatus>('/calendar/source/refresh', undefined, { params: { dashboardId } }),
};
