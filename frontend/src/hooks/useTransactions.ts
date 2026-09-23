import { useState, useCallback, useRef } from 'react';
import { transactionsApi } from '../api/transactions';
import { useDashboards } from './useDashboards';
import type { Transaction, CreateTransaction, UpdateTransaction, TransactionSummary, TransactionFilters } from '../types/transaction';

/** Le total du périmètre filtré, lu dans X-Total-Count. Absent quand la requête n'était pas paginée. */
const totalFromHeaders = (headers: Record<string, unknown>): number | null => {
  const raw = headers['x-total-count'];
  if (raw === undefined || raw === null) return null;
  const n = Number(raw);
  return Number.isFinite(n) ? n : null;
};

export type FetchMode = 'replace' | 'append' | 'refresh';

export const useTransactions = () => {
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [total, setTotal] = useState<number | null>(null);
  const [summary, setSummary] = useState<TransactionSummary | null>(null);
  const [loading, setLoading] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { currentDashboard } = useDashboards();
  // Numéro de la dernière requête partie : une réponse en retard (filtre changé entre-temps) est ignorée,
  // sinon elle remplacerait la liste courante ou, en mode append, y collerait une page d'un autre périmètre.
  const requestSeq = useRef(0);

  const dashboardId = currentDashboard?.id;

  // Trois façons de charger :
  // - replace : nouveaux filtres, la liste est remplacée et l'écran affiche son chargement pendant la requête ;
  // - append : page suivante, collée à la fin de la liste ;
  // - refresh : rechargement après une mutation locale, la liste reste affichée et « Voir plus » est bloqué.
  const fetchTransactions = useCallback(async (filters?: TransactionFilters, options?: { mode?: FetchMode }) => {
    const mode = options?.mode ?? 'replace';
    const seq = ++requestSeq.current;
    // La requête qui prend la main pose les deux drapeaux : un « Voir plus » dépassé par un changement de
    // filtre ne laisse pas loadingMore à true, puisque la requête de remplacement l'a remis à false ici.
    setLoading(mode === 'replace');
    setLoadingMore(mode !== 'replace');
    setError(null);
    try {
      const response = await transactionsApi.getAll({ ...filters, dashboardId });
      if (seq !== requestSeq.current) return;
      setTransactions((prev) => (mode === 'append' ? [...prev, ...response.data] : response.data));
      setTotal(totalFromHeaders(response.headers as Record<string, unknown>));
    } catch {
      if (seq !== requestSeq.current) return;
      setError('Erreur lors du chargement des transactions');
    } finally {
      // Une réponse en retard ne touche à aucun drapeau : ils appartiennent à la requête courante.
      if (seq === requestSeq.current) {
        setLoading(false);
        setLoadingMore(false);
      }
    }
  }, [dashboardId]);

  const fetchSummary = useCallback(async () => {
    try {
      const response = await transactionsApi.getSummary(dashboardId);
      setSummary(response.data);
    } catch {
      setError('Erreur lors du chargement du résumé');
    }
  }, [dashboardId]);

  // Création et suppression modifient la liste en place pour un retour immédiat, puis l'écran recharge la
  // liste depuis le serveur (mode refresh) : la position de la ligne côté serveur n'est pas connue ici, et
  // toute page suivante calculée sur une liste modifiée localement sauterait ou répéterait une ligne.
  const createTransaction = async (data: CreateTransaction) => {
    const response = await transactionsApi.create(data);
    setTransactions((prev) => [response.data, ...prev]);
    setTotal((prev) => (prev === null ? null : prev + 1));
    return response.data;
  };

  const updateTransaction = async (id: number, data: UpdateTransaction) => {
    const response = await transactionsApi.update(id, data);
    setTransactions((prev) =>
      prev.map((t) => (t.id === id ? response.data : t))
    );
    return response.data;
  };

  const deleteTransaction = async (id: number) => {
    await transactionsApi.delete(id);
    setTransactions((prev) => prev.filter((t) => t.id !== id));
    setTotal((prev) => (prev === null ? null : Math.max(0, prev - 1)));
  };

  const setExceptional = async (id: number, isExceptional: boolean) => {
    const response = await transactionsApi.setExceptional(id, isExceptional);
    setTransactions((prev) =>
      prev.map((t) => (t.id === id ? response.data : t))
    );
    return response.data;
  };

  const setFixed = async (id: number, isFixed: boolean) => {
    const response = await transactionsApi.setFixed(id, isFixed);
    setTransactions((prev) =>
      prev.map((t) => (t.id === id ? response.data : t))
    );
    return response.data;
  };

  const setRefund = async (id: number, isRefund: boolean) => {
    const response = await transactionsApi.setRefund(id, isRefund);
    setTransactions((prev) =>
      prev.map((t) => (t.id === id ? response.data : t))
    );
    return response.data;
  };

  const setEnvelope = async (id: number, projectEnvelopeId: number | null) => {
    const response = await transactionsApi.setEnvelope(id, projectEnvelopeId);
    setTransactions((prev) =>
      prev.map((t) => (t.id === id ? response.data : t))
    );
    return response.data;
  };

  // Pas de chargement automatique ici : l'écran Transactions, seul consommateur, déclenche le sien avec
  // ses filtres, sa période et sa page. L'ancien effet partait en plus, sans limite, sur tout le périmètre.

  return {
    transactions,
    total,
    summary,
    loading,
    loadingMore,
    error,
    fetchTransactions,
    fetchSummary,
    createTransaction,
    updateTransaction,
    deleteTransaction,
    setExceptional,
    setFixed,
    setRefund,
    setEnvelope,
  };
};
