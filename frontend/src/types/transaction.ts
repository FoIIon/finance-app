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
  bankAccountName?: string;
  bankInstitutionName?: string;
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

export interface TransactionFilters {
  dashboardId?: number;
  from?: string;
  to?: string;
  categoryId?: number;
  type?: TransactionType;
  accountId?: number;
  bankAccountId?: number;
  search?: string;
  sortBy?: string;
  sortDesc?: boolean;
}
