// Miroir des DTO de AgendaController, CalendarController et EcheanceController.
// Contrat figé côté backend (Services/Reporting/AgendaModels.cs) : ne rien renommer ici.

export type AgendaView = 'week' | 'month';

export type AgendaKind = 'event' | 'echeance' | 'recurring' | 'missing';

/** Dérivé à la requête par le serveur, jamais recalculé ici. */
export type AgendaStatus = 'late' | 'due' | 'paid' | 'planned';

export interface AgendaItem {
  /** event:<uid>:<startUtcIso> | echeance:<id> | recurring:<id>:<yyyy-MM-dd> | missing:<uid>:<yyyy-MM-dd> */
  id: string;
  kind: AgendaKind;
  /** yyyy-MM-dd dans le fuseau du ménage. */
  date: string;
  /** HH:mm, null sans heure. */
  start: string | null;
  end: string | null;
  isAllDay: boolean;
  title: string;
  location: string | null;
  amount: number | null;
  status: AgendaStatus | null;
  /** Posée seulement sur un retard porté dans le jour courant : la date d'origine. */
  originalDate: string | null;
  echeanceId: number | null;
  transactionId: number | null;
  isRoutine: boolean;
}

export interface AgendaDay {
  date: string;
  isToday: boolean;
  items: AgendaItem[];
  routine: AgendaItem[];
}

export interface AgendaEmptyRange {
  from: string;
  to: string;
}

export interface AgendaUpcoming {
  from: string;
  to: string;
  items: AgendaItem[];
}

export type CalendarSyncStatus = 'Ok' | 'HttpError' | 'Invalid' | 'KeyLost' | 'Pending';

/** État de la source de calendrier. L'adresse ICS n'y figure jamais. */
export interface CalendarStatus {
  connected: boolean;
  calendarName: string | null;
  /** Dernière synchronisation réussie, ISO UTC. Null tant qu'aucune n'a abouti. */
  lastSyncAt: string | null;
  /** Dernière tentative, réussie ou non, ISO UTC. */
  lastAttemptAt: string | null;
  lastSyncStatus: CalendarSyncStatus | null;
  lastError: string | null;
}

export interface AgendaResult {
  view: AgendaView;
  from: string;
  to: string;
  today: string;
  timeZone: string;
  calendar: CalendarStatus;
  days: AgendaDay[];
  emptyRanges: AgendaEmptyRange[];
  upcoming: AgendaUpcoming;
}

/** EcheancePaymentDto : ce que la fiche montre de la transaction qui règle l'échéance. */
export interface EcheancePayment {
  transactionId: number;
  /** yyyy-MM-dd dans le fuseau du ménage. */
  date: string;
  amount: number;
  description: string;
  counterpartyName: string | null;
}

/** EcheanceDto : la ligne complète, lue pour la feuille basse et pour le PUT de remplacement. */
export interface Echeance {
  id: number;
  dashboardId: number;
  label: string;
  dueDate: string;
  amount: number | null;
  isAmountKnown: boolean;
  notes: string | null;
  /** AVenir, EnRetard ou Payee. Calculé à la lecture. */
  status: string;
  paidAt: string | null;
  transactionId: number | null;
  /** IBAN du bénéficiaire, normalisé par le serveur (sans espaces, majuscules). */
  counterpartyIban: string | null;
  /** Les douze chiffres de la communication structurée attendue. */
  structuredCommunication: string | null;
  /** Nom du bénéficiaire pour le QR code de virement, 70 caractères au plus. Null si non renseigné. */
  counterpartyName: string | null;
  /** Instant ISO UTC du rapprochement automatique. Null quand le lien est manuel ou absent. */
  matchedAt: string | null;
  /** Instant ISO UTC où un rapprochement automatique a été défait : le serveur ne redevine plus tant qu'une clé ne change pas. */
  autoMatchRefusedAt: string | null;
  /**
   * La transaction liée, pour l'affichage. Null sans lien, et toujours null dans la liste (GET /echeances,
   * qui ne charge pas la transaction) : seule la fiche (GET /echeances/{id}) le remplit.
   */
  payment: EcheancePayment | null;
  documentIds: number[];
  createdByUserId: number;
  createdAt: string;
  updatedAt: string;
}

/**
 * UpdateEcheanceDto : remplacement complet des champs saisis, un champ absent revient à null. Le lien de
 * paiement n'en fait pas partie : seuls « Je l'ai payée » (pay) et « Finalement non » (unpay) le changent.
 */
export interface UpdateEcheance {
  label: string;
  dueDate: string;
  amount: number | null;
  notes: string | null;
  /** Saisie brute acceptée, le serveur normalise. Null : effacé. */
  counterpartyIban: string | null;
  structuredCommunication: string | null;
  /** Le serveur trime, refuse au-delà de 70 caractères. Null : effacé. */
  counterpartyName: string | null;
}
