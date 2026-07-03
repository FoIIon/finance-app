export const TransactionType = {
  Income: 0,
  Expense: 1,
} as const;

export type TransactionType = (typeof TransactionType)[keyof typeof TransactionType];

export interface Transaction {
  id: number;
  amount: number;
  description: string;
  date: string;
  type: TransactionType;
  categoryId: number;
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  accountId: number;
  accountName: string;
  externalId?: string;
  isImported: boolean;
  counterpartyName?: string;
  isExceptional: boolean;
  bankAccountName?: string;
  bankInstitutionName?: string;
  projectEnvelopeId?: number | null;
  projectEnvelopeName?: string | null;
}

export interface CreateTransaction {
  amount: number;
  description: string;
  date: string;
  type: TransactionType;
  categoryId: number;
  accountId: number;
}

export interface UpdateTransaction {
  amount: number;
  description: string;
  date: string;
  type: TransactionType;
  categoryId: number;
  accountId: number;
}

export interface TransactionSummary {
  totalIncome: number;
  totalExpenses: number;
  balance: number;
  /** Somme des dépenses sur catégories transfert (Épargne, etc.) — exclues du Balance. */
  totalSavings: number;
  /** Somme des dépenses exceptionnelles (non-transfert) de la période — toujours calculée. */
  exceptionalExpenses: number;
  categoryBreakdown: CategoryBreakdown[];
  /** Détail des mises de côté par catégorie de transfert. */
  savingsBreakdown: CategoryBreakdown[];
  monthlyBalance: MonthlyBalance[];
}

export interface CategoryBreakdown {
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  amount: number;
  percentage: number;
  categoryId: number;
}

export interface MonthlyBalance {
  month: string;
  income: number;
  expenses: number;
  balance: number;
  totalBalance: number;
}

export interface CategoryMonthHistory {
  /** Clé triable "yyyy-MM". */
  month: string;
  /** Libellé court fr-FR "juil. 2026". */
  label: string;
  /** Total dépenses (courant + exceptionnel). */
  total: number;
  /** Dépenses hors exceptionnel. */
  currentTotal: number;
  /** Dépenses exceptionnelles seules. */
  exceptionalTotal: number;
}

export interface MonthlyReport {
  year: number;
  month: number;
  /** Revenus non-transfert du mois. */
  entrees: number;
  /** Dépenses non-transfert sur catégories marquées « charge fixe ». */
  fixe: number;
  /** Dépenses sur catégories transfert (épargne, virements perso). */
  misesDeCote: number;
  /** Dépenses non-transfert non-fixes (le reste). */
  variable: number;
  /** Part exceptionnelle du bloc variable. */
  variableExceptionnel: number;
  /** entrees − fixe − misesDeCote − variable. */
  total: number;
  entreesByCategory: CategoryBreakdown[];
  fixeByCategory: CategoryBreakdown[];
  misesDeCoteByCategory: CategoryBreakdown[];
  variableByCategory: CategoryBreakdown[];
}

export interface BurndownDay {
  /** Jour du mois (1 → dernier jour). */
  day: number;
  /** Date ISO "yyyy-MM-dd". */
  date: string;
  /** Cumul dépenses non-transfert jusqu'à ce jour inclus. null si jour futur. */
  spent: number | null;
  /** Cumul entrées non-transfert jusqu'à ce jour inclus. null si jour futur. */
  income: number | null;
  /** income − spent. null si jour futur. */
  remaining: number | null;
}

export interface Burndown {
  year: number;
  month: number;
  days: BurndownDay[];
  /** Reste au jour courant (mois courant) ou valeur finale (mois passé). */
  remainingToday: number;
  /** Moyenne journalière des dépenses variables des 14 derniers jours. */
  dailyPaceVariable: number;
  /** Dépenses récurrentes restant à tomber ce mois-ci. */
  upcomingRecurringExpenses: number;
  /** Entrées récurrentes restant à tomber ce mois-ci. */
  upcomingRecurringIncome: number;
  /** true si les récurrentes ont pu être intégrées à la projection. */
  recurringIncluded: boolean;
  /** Projection de fin de mois (valeur finale réelle si mois passé). */
  projectedEndOfMonth: number;
  /** Jours restants après aujourd'hui (0 si mois passé). */
  daysRemaining: number;
  /** true = mois entièrement passé. */
  isPast: boolean;
  /** Jour « aujourd'hui », ancre de la projection. null hors mois courant. */
  todayDay: number | null;
}

export interface TransactionFilters {
  dashboardId?: number;
  from?: string;
  to?: string;
  categoryId?: number;
  type?: TransactionType;
  accountId?: number;
  bankAccountId?: number;
  isExceptional?: boolean;
  search?: string;
  sortBy?: string;
  sortDesc?: boolean;
}
