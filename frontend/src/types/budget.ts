export interface Budget {
  id: number;
  dashboardId: number;
  categoryId: number;
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  amount: number;
  period: 'Monthly' | 'Yearly';
  createdAt: string;
}

export interface BudgetProgress {
  budgetId: number;
  categoryId: number;
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  budget: number;
  spent: number;
  remaining: number;
  progressPct: number;
  status: 'ok' | 'warning' | 'exceeded';
}

export interface CreateBudget {
  dashboardId: number;
  categoryId: number;
  amount: number;
  period?: 'Monthly' | 'Yearly';
}

export interface UpdateBudget {
  amount?: number;
  period?: 'Monthly' | 'Yearly';
}
