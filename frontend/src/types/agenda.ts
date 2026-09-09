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
  documentIds: number[];
  createdByUserId: number;
  createdAt: string;
  updatedAt: string;
}

/** UpdateEcheanceDto : remplacement complet, un champ absent revient à null. */
export interface UpdateEcheance {
  label: string;
  dueDate: string;
  amount: number | null;
  notes: string | null;
  transactionId: number | null;
}
