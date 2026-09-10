// Mise en forme et contrôles d'avant-envoi des documents. Le serveur reste juge (type par les octets de
// tête, taille, quota) : ici on épargne seulement un envoi de 20 Mo voué au refus.
import { isAxiosError } from 'axios';
import type { DocumentKind, DuplicateDocument } from '../../types/documents';

export const DOCUMENT_KIND_LABELS: Record<DocumentKind, string> = {
  Facture: 'Facture',
  Fiscal: 'Fiscal',
  Contrat: 'Contrat',
  Autre: 'Autre',
};

/** Documents:MaxFileBytes dans appsettings.json (20 971 520). Le serveur répond 413 au-delà, on prévient avant. */
export const MAX_FILE_BYTES = 20 * 1024 * 1024;

const ACCEPTED_EXTENSIONS = ['pdf', 'jpg', 'jpeg', 'png'];
const ACCEPTED_TYPES = ['application/pdf', 'image/jpeg', 'image/png'];

const fmt = (n: number, digits: number) =>
  new Intl.NumberFormat('fr-FR', { maximumFractionDigits: digits }).format(n);

/** « 512 o », « 245 Ko », « 1,2 Mo ». Base 1024, comme la limite du serveur. */
export const formatBytes = (bytes: number) => {
  if (bytes < 1024) return `${bytes} o`;
  if (bytes < 1024 * 1024) return `${fmt(bytes / 1024, 0)} Ko`;
  return `${fmt(bytes / (1024 * 1024), 1)} Mo`;
};

/** « facture-luminus.pdf » → « facture-luminus », pour pré-remplir un libellé. */
export const fileNameWithoutExtension = (name: string) => name.replace(/\.[^.]+$/, '');

const extensionOf = (name: string) => name.split('.').pop()?.toLowerCase() ?? '';

/**
 * Ce qu'on peut savoir avant d'envoyer : une photo HEIC (l'iPhone en produit par défaut), un fichier au-delà
 * de la limite, une extension hors PDF, JPEG, PNG. Null si rien ne s'y oppose.
 */
export const preflightFile = (file: File): string | null => {
  const ext = extensionOf(file.name);
  if (ext === 'heic' || ext === 'heif' || file.type.startsWith('image/heic') || file.type.startsWith('image/heif')) {
    return 'Photo en HEIC : prends la photo en JPEG (Réglages › Appareil photo › Formats › Le plus compatible).';
  }
  if (file.size > MAX_FILE_BYTES) {
    return `Fichier trop lourd (${formatBytes(file.size)}), maximum ${formatBytes(MAX_FILE_BYTES)}.`;
  }
  if (!ACCEPTED_TYPES.includes(file.type) && !ACCEPTED_EXTENSIONS.includes(ext)) {
    return 'Seuls les PDF, JPEG et PNG sont acceptés.';
  }
  return null;
};

/** Le corps du 409 d'un envoi, si c'en est un. */
export const duplicateOf = (err: unknown): DuplicateDocument | null => {
  if (!isAxiosError(err) || err.response?.status !== 409) return null;
  const data: unknown = err.response.data;
  if (data && typeof data === 'object' && 'existingDocumentId' in data && typeof data.existingDocumentId === 'number') {
    return { existingDocumentId: data.existingDocumentId, message: 'message' in data && typeof data.message === 'string' ? data.message : 'Ce fichier est déjà rangé.' };
  }
  return null;
};

/** Le texte du serveur tel quel (400, 413, 415, 507, 410), sinon une phrase neutre. */
export const serverMessageOf = (err: unknown, fallback: string): string => {
  if (!isAxiosError(err)) return fallback;
  const data: unknown = err.response?.data;
  if (typeof data === 'string' && data.trim()) return data;
  if (data && typeof data === 'object' && 'errors' in data && data.errors && typeof data.errors === 'object') {
    const first = Object.values(data.errors as Record<string, unknown>).flat().map(String).find(Boolean);
    if (first) return first;
  }
  switch (err.response?.status) {
    case 404: return 'Introuvable : le document ou son échéance n\'existe plus.';
    // Le plafond de requête de Kestrel tranche avant le contrôleur, sans corps.
    case 413: return `Fichier au-delà de ${formatBytes(MAX_FILE_BYTES)}.`;
    default: return fallback;
  }
};
