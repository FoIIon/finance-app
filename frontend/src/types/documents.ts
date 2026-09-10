// Miroir des DTO de DocumentController. Contrat figé côté backend : ne rien renommer ici.

/** DocumentKind, stocké et transmis en texte. */
export type DocumentKind = 'Facture' | 'Fiscal' | 'Contrat' | 'Autre';

export const DOCUMENT_KINDS: readonly DocumentKind[] = ['Facture', 'Fiscal', 'Contrat', 'Autre'];

/** DocumentDto. Le type réel (contentType) est celui déduit par le serveur des octets de tête. */
export interface Document {
  id: number;
  dashboardId: number;
  echeanceId: number | null;
  kind: DocumentKind;
  fiscalYear: number | null;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  sha256: string;
  uploadedByUserId: number;
  /** ISO UTC. */
  createdAt: string;
}

/** UploadDocumentDto, envoyé en multipart/form-data. Le fichier part tel quel, le serveur le type lui-même. */
export interface UploadDocument {
  file: File;
  dashboardId: number;
  kind: DocumentKind;
  fiscalYear: number | null;
  echeanceId: number | null;
}

/** UpdateDocumentDto : remplacement des trois métadonnées, un champ absent revient à null. Le fichier ne change pas. */
export interface UpdateDocument {
  kind: DocumentKind;
  fiscalYear: number | null;
  echeanceId: number | null;
}

/** Corps du 409 quand le même contenu (SHA-256) existe déjà dans le dashboard. */
export interface DuplicateDocument {
  existingDocumentId: number;
  message: string;
}
