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
  categoryBreakdown: CategoryBreakdown[];
  monthlyBalance: MonthlyBalance[];
}

export interface CategoryBreakdown {
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  amount: number;
  percentage: number;
}

export interface MonthlyBalance {
  month: string;
  income: number;
  expenses: number;
  balance: number;
}

export interface TransactionFilters {
  dashboardId?: number;
  from?: string;
  to?: string;
  categoryId?: number;
  type?: TransactionType;
  accountId?: number;
  search?: string;
  sortBy?: string;
  sortDesc?: boolean;
}
