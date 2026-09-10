import type { AxiosProgressEvent } from 'axios';
import apiClient from './client';
import type { Document, DocumentKind, UpdateDocument, UploadDocument } from '../types/documents';

export interface DocumentFilters {
  fiscalYear?: number;
  kind?: DocumentKind;
  echeanceId?: number;
}

export const documentsApi = {
  getAll: (dashboardId: number, filters: DocumentFilters = {}) =>
    apiClient.get<Document[]>('/documents', { params: { dashboardId, ...filters } }),

  getById: (id: number) => apiClient.get<Document>(`/documents/${id}`),

  /**
   * multipart/form-data : axios pose lui-même le Content-Type avec la frontière, on ne le force pas.
   * Les noms de champs sont ceux d'UploadDocumentDto, la liaison est insensible à la casse.
   */
  upload: (data: UploadDocument, onUploadProgress?: (e: AxiosProgressEvent) => void) => {
    const form = new FormData();
    form.append('File', data.file, data.file.name);
    form.append('DashboardId', String(data.dashboardId));
    form.append('Kind', data.kind);
    if (data.fiscalYear != null) form.append('FiscalYear', String(data.fiscalYear));
    if (data.echeanceId != null) form.append('EcheanceId', String(data.echeanceId));
    return apiClient.post<Document>('/documents', form, { onUploadProgress });
  },

  /**
   * Le fichier lui-même, avec le jeton Bearer : un href ou un window.open direct sur l'URL rend 401.
   * L'appelant en fait un object URL, jamais une URL qui porterait le jeton.
   */
  content: (id: number) => apiClient.get<Blob>(`/documents/${id}/content`, { responseType: 'blob' }),

  update: (id: number, data: UpdateDocument) => apiClient.put<Document>(`/documents/${id}`, data),

  remove: (id: number) => apiClient.delete<void>(`/documents/${id}`),
};
