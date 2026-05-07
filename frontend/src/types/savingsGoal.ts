export interface SavingsGoal {
  id: number;
  dashboardId: number;
  name: string;
  icon: string;
  targetAmount: number;
  currentAmount: number;
  isAutoCalculated: boolean;
  targetDate: string;
  categoryId?: number;
  categoryName?: string;
  categoryIcon?: string;
  createdAt: string;
}

export interface SavingsGoalProgress {
  id: number;
  name: string;
  icon: string;
  targetAmount: number;
  currentAmount: number;
  isAutoCalculated: boolean;
  remaining: number;
  progressPct: number;
  targetDate: string;
  daysRemaining: number;
  monthsRemaining: number;
  requiredMonthlySavings: number;
}

export interface CreateSavingsGoal {
  dashboardId: number;
  name: string;
  icon: string;
  targetAmount: number;
  targetDate: string;
  categoryId?: number;
}

export interface UpdateSavingsGoal {
  name?: string;
  icon?: string;
  targetAmount?: number;
  currentAmount?: number;
  targetDate?: string;
  categoryId?: number;
}

export interface AccountBalance {
  accountId: number;
  accountName: string;
  bankInstitutionName?: string;
  balance: number;
  isRealBalance: boolean;
  isManual: boolean;
  lastTransactionDate?: string;
  balanceUpdatedAt?: string;
}

export interface ManualAccount {
  id: number;
  name: string;
  iban: string;
  initialBalance: number;
  initialBalanceDate: string;
  sourceBankAccountId?: number;
  sourceBankAccountName?: string;
  incrementCategoryId?: number;
  incrementCategoryName?: string;
}

export interface CreateManualAccount {
  name: string;
  iban?: string;
  currency?: string;
  initialBalance: number;
  initialBalanceDate?: string;
  sourceBankAccountId?: number;
  incrementCategoryId?: number;
}

export interface UpdateManualAccount {
  name?: string;
  iban?: string;
  initialBalance?: number;
  initialBalanceDate?: string;
  sourceBankAccountId?: number;
  incrementCategoryId?: number;
}
