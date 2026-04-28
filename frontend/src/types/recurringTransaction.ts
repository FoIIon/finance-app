export type RecurringFrequency = 'Weekly' | 'Monthly' | 'Yearly';
export type RecurringType = 'Income' | 'Expense';

export interface RecurringTransaction {
  id: number;
  dashboardId: number;
  accountId: number | null;
  accountName: string | null;
  categoryId: number | null;
  categoryName: string | null;
  categoryIcon: string | null;
  categoryColor: string | null;
  description: string;
  amount: number;
  type: RecurringType;
  frequency: RecurringFrequency;
  dayOfMonth: number | null;
  startDate: string; // ISO date string (DateOnly)
  endDate: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateRecurringTransaction {
  dashboardId: number;
  accountId?: number | null;
  categoryId?: number | null;
  description: string;
  amount: number;
  type: RecurringType;
  frequency: RecurringFrequency;
  dayOfMonth?: number | null;
  startDate: string;
  endDate?: string | null;
}

export interface UpdateRecurringTransaction {
  accountId?: number | null;
  categoryId?: number | null;
  description?: string;
  amount?: number;
  type?: RecurringType;
  frequency?: RecurringFrequency;
  dayOfMonth?: number | null;
  startDate?: string;
  endDate?: string | null;
  isActive?: boolean;
}
