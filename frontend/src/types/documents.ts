// Miroir des DTO de DocumentController. Contrat figé côté backend : ne rien renommer ici.

/** DocumentKind, stocké et transmis en texte. */
export type DocumentKind = 'Facture' | 'Fiscal' | 'Contrat' | 'Autre';

export const DOCUMENT_KINDS: readonly DocumentKind[] = ['Facture', 'Fiscal', 'Contrat', 'Autre'];

/** DocumentSource : déposé par un membre, ou pièce jointe d'un mail relevé par le serveur. */
export type DocumentSource = 'Upload' | 'Mail';

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
  /** Null pour un document reçu par mail. */
  uploadedByUserId: number | null;
  /** ISO UTC. */
  createdAt: string;
  source: DocumentSource;
}

/** MailSyncStatus, en texte. */
export type MailSyncStatus = 'Pending' | 'Ok' | 'AuthError' | 'ConnectionError' | 'Error';

/** MailSourceDto : l'état de la boîte factures. Adresse relevée et dates, jamais un secret ni un objet de mail. */
export interface MailSource {
  address: string;
  /** ISO UTC, dernière tentative. */
  lastAttemptAt: string | null;
  /** ISO UTC, dernier relevé qui a ouvert la boîte. */
  lastSyncAt: string | null;
  lastSyncStatus: MailSyncStatus;
  lastError: string | null;
  /** ISO UTC, dernier document déposé. */
  lastDepositAt: string | null;
  depositedCount: number;
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
