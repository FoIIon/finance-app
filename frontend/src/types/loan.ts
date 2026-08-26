export const LoanKind = {
  Mortgage: 0,
  Family: 1,
  Consumer: 2,
} as const;

export interface Loan {
  id: number;
  dashboardId: number;
  name: string;
  holder: string;
  kind: number;
  lender: string | null;
  reference: string | null;
  initialPrincipal: number | null;
  annualRatePercent: number;
  monthlyPayment: number;
  /** Échéance de référence : tout le tableau d'amortissement en dérive. */
  anchorDate: string;
  /** Capital restant dû juste après l'échéance d'ancrage. */
  anchorPrincipal: number;
  debitIban: string | null;
  isArchived: boolean;

  // Dérivé côté serveur, jamais stocké
  remainingPrincipal: number;
  remainingInstallments: number;
  finalDueDate: string | null;
  remainingInterest: number;
  remainingPayments: number;
  nextDueDate: string | null;
  nextPayment: number | null;
  repaidPercent: number | null;
}

export interface LoanInstallment {
  dueDate: string;
  payment: number;
  interest: number;
  principal: number;
  remainingPrincipal: number;
}

export interface DebtSummary {
  totalRemainingPrincipal: number;
  totalMonthlyPayment: number;
  totalRemainingInterest: number;
  debtFreeDate: string | null;
  loanCount: number;
}

export interface CreateLoan {
  dashboardId: number;
  name: string;
  holder: string;
  kind: number;
  lender?: string | null;
  reference?: string | null;
  initialPrincipal?: number | null;
  annualRatePercent: number;
  monthlyPayment: number;
  anchorDate: string;
  anchorPrincipal: number;
  debitIban?: string | null;
}

export type UpdateLoan = Partial<Omit<CreateLoan, 'dashboardId'>> & { isArchived?: boolean };
