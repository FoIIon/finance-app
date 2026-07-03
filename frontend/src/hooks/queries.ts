import { useQuery } from '@tanstack/react-query';
import { useContext } from 'react';
import { transactionsApi } from '../api/transactions';
import { budgetsApi } from '../api/budgets';
import { savingsGoalsApi, dashboardExtrasApi } from '../api/savingsGoals';
import type { Period } from '../utils/periods';
import { periodToRange } from '../utils/periods';
import { PeriodContext } from '../context/PeriodContext';

// Helper local — on lit le filtre depuis le context sans dépendre du sous-fichier (évite la circularité)
const useBankFilter = () => useContext(PeriodContext).bankAccountFilter;

export const useSummaryQuery = (dashboardId: number | undefined, period: Period) => {
  const bankAccountId = useBankFilter();
  const includeExceptional = useContext(PeriodContext).includeExceptional;
  return useQuery({
    queryKey: ['summary', dashboardId, period.key, bankAccountId, includeExceptional],
    enabled: !!dashboardId,
    queryFn: async () => {
      const { from, to } = periodToRange(period);
      const res = await transactionsApi.getSummary(dashboardId, from, to, bankAccountId, includeExceptional);
      return res.data;
    },
  });
};

export const useBudgetsProgressQuery = (dashboardId: number | undefined, period: Period) =>
  useQuery({
    queryKey: ['budgets-progress', dashboardId, period.key],
    enabled: !!dashboardId,
    queryFn: async () => {
      const { from, to } = periodToRange(period);
      const res = await budgetsApi.getProgress(dashboardId!, from, to);
      return res.data;
    },
  });

export const useBudgetsQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['budgets', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await budgetsApi.getAll(dashboardId!);
      return res.data;
    },
  });

export const useSavingsGoalsProgressQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['savings-goals-progress', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await savingsGoalsApi.getProgress(dashboardId!);
      return res.data;
    },
  });

export const useSavingsGoalsQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['savings-goals', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await savingsGoalsApi.getAll(dashboardId!);
      return res.data;
    },
  });

/**
 * Solde par compte bancaire. Si `period` est fourni, calcule le solde rétrospectif à la borne
 * `to` de la période ; sinon retourne le solde courant.
 */
export const useAccountBalancesQuery = (dashboardId: number | undefined, period?: Period) =>
  useQuery({
    queryKey: ['account-balances', dashboardId, period?.key ?? 'now'],
    enabled: !!dashboardId,
    queryFn: async () => {
      const to = period ? periodToRange(period).to : undefined;
      const res = await dashboardExtrasApi.getAccountBalances(dashboardId!, to);
      return res.data;
    },
  });

export const useRecentTransactionsQuery = (dashboardId: number | undefined, limit = 5) => {
  const bankAccountId = useBankFilter();
  return useQuery({
    queryKey: ['recent-transactions', dashboardId, limit, bankAccountId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await dashboardExtrasApi.getRecentTransactions(dashboardId!, limit, bankAccountId);
      return res.data;
    },
  });
};

export const useUncategorizedQuery = (dashboardId: number | undefined, limit = 50) => {
  const bankAccountId = useBankFilter();
  return useQuery({
    queryKey: ['uncategorized', dashboardId, limit, bankAccountId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await dashboardExtrasApi.getUncategorized(dashboardId!, limit, bankAccountId);
      return res.data;
    },
  });
};

export const useAnomaliesQuery = (dashboardId: number | undefined, period: Period) =>
  useQuery({
    queryKey: ['anomalies', dashboardId, period.key],
    enabled: !!dashboardId,
    queryFn: async () => {
      const { from, to } = periodToRange(period);
      const res = await dashboardExtrasApi.getAnomalies(dashboardId!, from, to);
      return res.data;
    },
  });
