import { useState, useEffect, useCallback } from 'react';
import axios from 'axios';
import { bankingApi, categoryRulesApi } from '../api/banking';
import type { BankConnection, Institution, CategoryRule, CreateCategoryRule, UpdateCategoryRule, TradeRepublicLoginRequest, TradeRepublicVerifyRequest } from '../types/banking';

export const useBanking = () => {
  const [connections, setConnections] = useState<BankConnection[]>([]);
  const [institutions, setInstitutions] = useState<Institution[]>([]);
  const [categoryRules, setCategoryRules] = useState<CategoryRule[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Slot dédié : trois fonctions du hook remettent error à null en entrant, et le
  // diagnostic du retour de banque est irrécupérable une fois le ?ref= retiré de l'URL.
  const [callbackError, setCallbackError] = useState<string | null>(null);

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
    // Le message du serveur nomme le statut de réquisition (rejet, expiration, autorisation
    // inachevée). Sans ce catch il partait dans une promesse rejetée et l'utilisateur voyait
    // une connexion muette. L'erreur se pose après le rafraîchissement, qui remet error à null.
    setCallbackError(null);
    let message: string | null = null;
    try {
      await bankingApi.callback(ref);
    } catch (err) {
      const serverMessage = axios.isAxiosError(err) ? err.response?.data : undefined;
      message = typeof serverMessage === 'string' && serverMessage.length > 0
        ? serverMessage
        : "La liaison bancaire n'a pas abouti.";
    }
    await fetchConnections();
    if (message) setCallbackError(message);
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
    callbackError,
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
