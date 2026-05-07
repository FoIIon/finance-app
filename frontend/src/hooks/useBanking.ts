import { useState, useEffect, useCallback } from 'react';
import { bankingApi, categoryRulesApi } from '../api/banking';
import type { BankConnection, Institution, CategoryRule, CreateCategoryRule, UpdateCategoryRule, TradeRepublicLoginRequest, TradeRepublicVerifyRequest } from '../types/banking';

export const useBanking = () => {
  const [connections, setConnections] = useState<BankConnection[]>([]);
  const [institutions, setInstitutions] = useState<Institution[]>([]);
  const [categoryRules, setCategoryRules] = useState<CategoryRule[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchConnections = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await bankingApi.getConnections();
      setConnections(response.data);
    } catch {
      setError('Erreur lors du chargement des connexions bancaires');
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchInstitutions = useCallback(async (country: string) => {
    setError(null);
    try {
      const response = await bankingApi.getInstitutions(country);
      setInstitutions(response.data);
    } catch {
      setError('Erreur lors du chargement des institutions');
    }
  }, []);

  const connectBank = async (data: { institutionId: string; institutionName: string; institutionLogo: string }) => {
    const response = await bankingApi.connect(data);
    return response.data.authorizationUrl;
  };

  const handleCallback = async (ref: string) => {
    await bankingApi.callback(ref);
    await fetchConnections();
  };

  const deleteConnection = async (id: number) => {
    await bankingApi.deleteConnection(id);
    setConnections((prev) => prev.filter((c) => c.id !== id));
  };

  const syncConnection = async (id: number) => {
    await bankingApi.syncConnection(id);
    await fetchConnections();
  };

  const reconnectConnection = async (id: number) => {
    const response = await bankingApi.reconnectConnection(id);
    return response.data.authorizationUrl;
  };

  const updateAccount = async (id: number, isActive: boolean) => {
    await bankingApi.updateAccount(id, { isActive });
    await fetchConnections();
  };

  const fetchCategoryRules = useCallback(async () => {
    setError(null);
    try {
      const response = await categoryRulesApi.getAll();
      setCategoryRules(response.data);
    } catch {
      setError('Erreur lors du chargement des règles de catégorisation');
    }
  }, []);

  const createCategoryRule = async (data: CreateCategoryRule) => {
    const response = await categoryRulesApi.create(data);
    setCategoryRules((prev) => [...prev, response.data]);
    return response.data;
  };

  const updateCategoryRule = async (id: number, data: UpdateCategoryRule) => {
    const response = await categoryRulesApi.update(id, data);
    setCategoryRules((prev) => prev.map((r) => (r.id === id ? response.data : r)));
    return response.data;
  };

  const deleteCategoryRule = async (id: number) => {
    await categoryRulesApi.delete(id);
    setCategoryRules((prev) => prev.filter((r) => r.id !== id));
  };

  const tradeRepublicLogin = async (data: TradeRepublicLoginRequest) => {
    const response = await bankingApi.tradeRepublicLogin(data);
    return response.data.connectionId;
  };

  const tradeRepublicVerify = async (data: TradeRepublicVerifyRequest) => {
    await bankingApi.tradeRepublicVerify(data);
    await fetchConnections();
  };

  useEffect(() => {
    fetchConnections();
  }, [fetchConnections]);

  return {
    connections,
    institutions,
    categoryRules,
    loading,
    error,
    fetchConnections,
    fetchInstitutions,
    connectBank,
    handleCallback,
    deleteConnection,
    syncConnection,
    reconnectConnection,
    updateAccount,
    fetchCategoryRules,
    createCategoryRule,
    updateCategoryRule,
    deleteCategoryRule,
    tradeRepublicLogin,
    tradeRepublicVerify,
  };
};
