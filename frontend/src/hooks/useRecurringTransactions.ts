import { useState, useEffect, useCallback } from 'react';
import { recurringTransactionsApi } from '../api/recurringTransactions';
import { useDashboards } from './useDashboards';
import type {
  RecurringTransaction,
  CreateRecurringTransaction,
  UpdateRecurringTransaction,
} from '../types/recurringTransaction';

export const useRecurringTransactions = () => {
  const [recurringTransactions, setRecurringTransactions] = useState<RecurringTransaction[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { currentDashboard } = useDashboards();

  const dashboardId = currentDashboard?.id;

  const fetchRecurring = useCallback(async () => {
    if (!dashboardId) return;
    setLoading(true);
    setError(null);
    try {
      const response = await recurringTransactionsApi.getAll(dashboardId);
      setRecurringTransactions(response.data);
    } catch {
      setError('Erreur lors du chargement des transactions récurrentes');
    } finally {
      setLoading(false);
    }
  }, [dashboardId]);

  const createRecurring = async (data: CreateRecurringTransaction) => {
    if (!dashboardId) throw new Error('Aucun dashboard sélectionné');
    const response = await recurringTransactionsApi.create(dashboardId, {
      ...data,
      dashboardId,
    });
    setRecurringTransactions((prev) => [response.data, ...prev]);
    return response.data;
  };

  const updateRecurring = async (id: number, data: UpdateRecurringTransaction) => {
    if (!dashboardId) throw new Error('Aucun dashboard sélectionné');
    const response = await recurringTransactionsApi.update(dashboardId, id, data);
    setRecurringTransactions((prev) =>
      prev.map((r) => (r.id === id ? response.data : r))
    );
    return response.data;
  };

  const deleteRecurring = async (id: number) => {
    if (!dashboardId) throw new Error('Aucun dashboard sélectionné');
    await recurringTransactionsApi.delete(dashboardId, id);
    // Soft delete — le backend met IsActive=false, on retire de la liste locale
    setRecurringTransactions((prev) => prev.filter((r) => r.id !== id));
  };

  const toggleActive = async (id: number, isActive: boolean) => {
    if (!dashboardId) throw new Error('Aucun dashboard sélectionné');
    const response = await recurringTransactionsApi.update(dashboardId, id, { isActive });
    setRecurringTransactions((prev) =>
      prev.map((r) => (r.id === id ? response.data : r))
    );
    return response.data;
  };

  useEffect(() => {
    if (dashboardId) {
      fetchRecurring();
    }
  }, [dashboardId, fetchRecurring]);

  return {
    recurringTransactions,
    loading,
    error,
    fetchRecurring,
    createRecurring,
    updateRecurring,
    deleteRecurring,
    toggleActive,
  };
};
