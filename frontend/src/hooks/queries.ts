import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { categoriesApi } from '../api/categories';
import { useContext } from 'react';
import { transactionsApi } from '../api/transactions';
import { savingsGoalsApi, dashboardExtrasApi } from '../api/savingsGoals';
import { projectEnvelopesApi } from '../api/projectEnvelopes';
import { shoppingItemsApi } from '../api/shoppingItems';
import { investmentsApi } from '../api/investments';
import { loansApi } from '../api/loans';
import { agendaApi } from '../api/agenda';
import { echeancesApi } from '../api/echeances';
import { recurringTransactionsApi } from '../api/recurringTransactions';
import { documentsApi, type DocumentFilters } from '../api/documents';
import { calendarApi } from '../api/calendar';
import type { AgendaView } from '../types/agenda';
import type { Period } from '../utils/periods';
import { periodToRange } from '../utils/periods';
import { PeriodContext } from '../context/period-context';

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

export const useProjectEnvelopesQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['project-envelopes', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await projectEnvelopesApi.getAll(dashboardId!);
      return res.data;
    },
  });

export const useShoppingItemsQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['shopping-items', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await shoppingItemsApi.getAll(dashboardId!);
      return res.data;
    },
  });

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

export const useInvestmentsQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['investments', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await investmentsApi.getAll(dashboardId!);
      return res.data;
    },
  });

export const useCashQuery = () =>
  useQuery({
    queryKey: ['tr-cash'],
    queryFn: async () => {
      const res = await investmentsApi.getCash();
      return res.data;
    },
  });

/** Depuis quand l'historique du dashboard est un bilan (première transaction bancaire). */
export const useCoverageQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['coverage', dashboardId],
    queryFn: async () => {
      const res = await transactionsApi.getCoverage(dashboardId);
      return res.data;
    },
    enabled: !!dashboardId,
    staleTime: 5 * 60 * 1000,
  });

export const useCategoriesQuery = () =>
  useQuery({
    queryKey: ['categories'],
    queryFn: async () => {
      const res = await categoriesApi.getAll();
      return res.data;
    },
  });

export const useInvestmentHistoryQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['investment-history', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await investmentsApi.getHistory(dashboardId!);
      return res.data;
    },
  });

export const useInvestmentValuationsQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['investment-valuations', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await investmentsApi.getAllValuations(dashboardId!);
      return res.data;
    },
  });

export const useLoansQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['loans', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await loansApi.getAll(dashboardId!);
      return res.data;
    },
  });

export const useDebtSummaryQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['debt-summary', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await loansApi.getSummary(dashboardId!);
      return res.data;
    },
  });

/** Échéancier d'un emprunt. Chargé seulement quand la ligne est dépliée. */
export const useLoanScheduleQuery = (loanId: number | undefined, months?: number) =>
  useQuery({
    queryKey: ['loan-schedule', loanId, months],
    enabled: !!loanId,
    queryFn: async () => {
      const res = await loansApi.getSchedule(loanId!, months);
      return res.data;
    },
  });

// ---------------------------------------------------------------------------------------------
// Lot 2 Agenda. Clés ['agenda', dashboardId, …] et ['calendar-source', dashboardId] : une
// mutation d'échéance ou de calendrier invalide par préfixe.
// ---------------------------------------------------------------------------------------------

/** L'agenda d'un dashboard. anchor absent : le serveur prend aujourd'hui. staleTime court, l'écran vit dans la journée. */
export const useAgendaQuery = (dashboardId: number | undefined, view: AgendaView, anchor: string | undefined) =>
  useQuery({
    queryKey: ['agenda', dashboardId, view, anchor],
    enabled: !!dashboardId,
    staleTime: 60 * 1000,
    placeholderData: keepPreviousData,
    queryFn: async () => {
      const res = await agendaApi.get(dashboardId!, view, anchor);
      return res.data;
    },
  });

/** État de la source de calendrier, sans l'adresse. */
export const useCalendarSourceQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['calendar-source', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await calendarApi.getSource(dashboardId!);
      return res.data;
    },
  });

/**
 * Les transactions du mois (yyyy-MM) qu'on peut désigner comme règlement d'une récurrente, chargées quand la
 * fiche Routine s'ouvre. Clé ['recurring-candidates', dashboardId, recurringId, month] : un lien posé ou
 * retiré invalide par préfixe.
 */
export const useRecurringCandidatesQuery = (dashboardId: number | undefined, recurringId: number | undefined, month: string) =>
  useQuery({
    queryKey: ['recurring-candidates', dashboardId, recurringId, month],
    enabled: !!dashboardId && !!recurringId,
    queryFn: async () => {
      const res = await agendaApi.recurringCandidates(recurringId!, dashboardId!, month);
      return res.data;
    },
  });

/** Une récurrente, pour le montant prévu de la fiche Routine quand l'item porte le montant réel. */
export const useRecurringQuery = (dashboardId: number | undefined, id: number | undefined) =>
  useQuery({
    queryKey: ['recurring', dashboardId, id],
    enabled: !!dashboardId && !!id,
    queryFn: async () => {
      const res = await recurringTransactionsApi.getById(dashboardId!, id!);
      return res.data;
    },
  });

/** Une échéance complète, chargée quand la feuille basse s'ouvre. */
export const useEcheanceQuery = (id: number | undefined) =>
  useQuery({
    queryKey: ['echeance', id],
    enabled: !!id,
    queryFn: async () => {
      const res = await echeancesApi.getById(id!);
      return res.data;
    },
  });

// ---------------------------------------------------------------------------------------------
// Lot 1 Échéances et documents. Clés ['documents', dashboardId, …] et ['echeances', dashboardId] :
// un envoi, une modification ou une suppression invalide par préfixe.
// ---------------------------------------------------------------------------------------------

/** Les documents d'un dashboard, filtrés par le serveur (année fiscale, nature, échéance). Ordre du serveur. */
export const useDocumentsQuery = (dashboardId: number | undefined, filters: DocumentFilters = {}) =>
  useQuery({
    queryKey: ['documents', dashboardId, filters.fiscalYear ?? null, filters.kind ?? null, filters.echeanceId ?? null],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await documentsApi.getAll(dashboardId!, filters);
      return res.data;
    },
  });

/** L'état de la boîte factures, null tant que le serveur n'en connaît pas (204). Une minute de fraîcheur. */
export const useMailSourceQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['mail-source', dashboardId],
    enabled: !!dashboardId,
    staleTime: 60 * 1000,
    queryFn: () => documentsApi.mailSource(dashboardId!),
  });

/** Toutes les échéances du dashboard, pour écrire le libellé sous un document rattaché. */
export const useEcheancesQuery = (dashboardId: number | undefined) =>
  useQuery({
    queryKey: ['echeances', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await echeancesApi.getAll(dashboardId!);
      return res.data;
    },
  });
