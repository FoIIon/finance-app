import { useState, useEffect, useCallback } from 'react';
import { transactionsApi } from '../api/transactions';
import { useDashboards } from './useDashboards';
import type { Transaction, CreateTransaction, UpdateTransaction, TransactionSummary, TransactionFilters } from '../types/transaction';

export const useTransactions = () => {
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [summary, setSummary] = useState<TransactionSummary | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { currentDashboard } = useDashboards();

  const dashboardId = currentDashboard?.id;

  const fetchTransactions = useCallback(async (filters?: TransactionFilters) => {
    setLoading(true);
    setError(null);
    try {
      const response = await transactionsApi.getAll({ ...filters, dashboardId });
      setTransactions(response.data);
    } catch {
      setError('Erreur lors du chargement des transactions');
    } finally {
      setLoading(false);
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

  const createTransaction = async (data: CreateTransaction) => {
    const response = await transactionsApi.create(data);
    setTransactions((prev) => [response.data, ...prev]);
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
  };

  useEffect(() => {
    if (dashboardId) {
      fetchTransactions();
    }
  }, [dashboardId, fetchTransactions]);

  return {
    transactions,
    summary,
    loading,
    error,
    fetchTransactions,
    fetchSummary,
    createTransaction,
    updateTransaction,
    deleteTransaction,
  };
};
