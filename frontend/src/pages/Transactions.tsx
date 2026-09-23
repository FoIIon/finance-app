import { useState, useEffect, useMemo, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useTransactions } from '../hooks/useTransactions';
import { useDashboards } from '../hooks/useDashboards';
import { accountsApi } from '../api/accounts';
import { useProjectEnvelopesQuery, useCategoriesQuery } from '../hooks/queries';
import { TransactionType } from '../types/transaction';
import type { CreateTransaction, Transaction, TransactionFilters } from '../types/transaction';
import type { Account } from '../types/dashboard';
import { formatCurrency } from '../utils/format';
import { useToast } from '../hooks/useToast';

/** Période par défaut de l'écran : nombre de mois en arrière, ou tout l'historique. */
type Period = '1' | '3' | '12' | 'all';
const PERIOD_KEY = 'transactions.period';
const PERIOD_DEFAULT: Period = '3';
const PAGE_SIZE = 100;
// Plafond de l'API (TransactionController.LimiteMaxParPage) : au-delà, le serveur ramène la page à cette taille.
const MAX_PAGE_LIMIT = 500;

const isPeriod = (v: string | null): v is Period => v === '1' || v === '3' || v === '12' || v === 'all';

const readStoredPeriod = (): Period => {
  try {
    const v = localStorage.getItem(PERIOD_KEY);
    return isPeriod(v) ? v : PERIOD_DEFAULT;
  } catch {
    return PERIOD_DEFAULT;
  }
};

/** Aujourd'hui moins n mois, en date locale YYYY-MM-DD. Le jour est ramené au dernier du mois cible s'il déborde. */
const monthsAgoIso = (n: number): string => {
  const now = new Date();
  const target = new Date(now.getFullYear(), now.getMonth() - n, 1);
  const lastDay = new Date(target.getFullYear(), target.getMonth() + 1, 0).getDate();
  target.setDate(Math.min(now.getDate(), lastDay));
  const mm = String(target.getMonth() + 1).padStart(2, '0');
  const dd = String(target.getDate()).padStart(2, '0');
  return `${target.getFullYear()}-${mm}-${dd}`;
};

const formatCount = (n: number) => n.toLocaleString('fr-FR');

const Transactions = () => {
  const { transactions, total, loading, loadingMore, fetchTransactions, createTransaction, updateTransaction, deleteTransaction, setExceptional, setFixed, setRefund, setEnvelope } = useTransactions();
  const { currentDashboard } = useDashboards();
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const { data: categories = [] } = useCategoriesQuery();
  const [accounts, setAccounts] = useState<Account[]>([]);
  // Le hook partagé remplace un chargement manuel qui, lui, ignorait les invalidations
  // de la clé project-envelopes déclenchées ailleurs dans la page.
  const { data: loadedEnvelopes } = useProjectEnvelopesQuery(currentDashboard?.id);
  const envelopes = useMemo(
    () => (loadedEnvelopes ?? []).filter((e) => !e.isArchived),
    [loadedEnvelopes],
  );
  const [showForm, setShowForm] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<number | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  // Filtres
  const [filterType, setFilterType] = useState<TransactionType | ''>('');
  const [filterCategory, setFilterCategory] = useState<number | ''>('');
  const [filterAccount, setFilterAccount] = useState<number | ''>('');
  const [searchInput, setSearchInput] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const searchTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [period, setPeriod] = useState<Period>(readStoredPeriod);
  // Une recherche non vide porte sur tout l'historique : celui qui tape « Colruyt » veut toutes les lignes.
  const searchActive = searchQuery.trim() !== '';

  // Tri
  const [sortBy, setSortBy] = useState('date');
  const [sortDesc, setSortDesc] = useState(true);

  // Édition inline
  const [editingRowId, setEditingRowId] = useState<number | null>(null);
  const [editValues, setEditValues] = useState<{ description: string; categoryId: number; projectEnvelopeId: number | null }>({ description: '', categoryId: 0, projectEnvelopeId: null });

  // Formulaire
  const [formData, setFormData] = useState<CreateTransaction>({
    amount: 0,
    description: '',
    date: new Date().toISOString().split('T')[0],
    type: TransactionType.Expense,
    categoryId: 0,
    accountId: 0,
  });

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setShowForm(false);
    };
    if (showForm) {
      document.addEventListener('keydown', handleEscape);
      return () => document.removeEventListener('keydown', handleEscape);
    }
  }, [showForm]);

  useEffect(() => {
    accountsApi.getAll().then((res) => setAccounts(res.data));
  }, []);

  const changePeriod = (value: Period) => {
    setPeriod(value);
    try {
      localStorage.setItem(PERIOD_KEY, value);
    } catch {
      // Stockage indisponible (navigation privée) : la période vaut pour la session, sans plus.
    }
  };

  // Les filtres de la requête, page exclue. Tout changement repart à la première page et remplace la liste.
  const baseFilters = useMemo(() => {
    const filters: TransactionFilters = { sortBy, sortDesc, limit: PAGE_SIZE };
    if (filterType !== '') filters.type = filterType;
    if (filterCategory !== '') filters.categoryId = filterCategory;
    if (filterAccount !== '') filters.accountId = filterAccount;
    if (searchActive) filters.search = searchQuery;
    else if (period !== 'all') filters.from = monthsAgoIso(Number(period));
    return filters;
  }, [filterType, filterCategory, filterAccount, searchQuery, searchActive, period, sortBy, sortDesc]);

  useEffect(() => {
    fetchTransactions({ ...baseFilters, offset: 0 });
  }, [baseFilters, fetchTransactions]);

  const hasMore = total !== null && transactions.length < total;

  // L'offset de la page suivante est la longueur de la liste : elle ne contient que des lignes venues du
  // serveur, puisque toute mutation locale est suivie d'un rechargement (reloadLoaded).
  const loadMore = () => {
    if (loading || loadingMore || !hasMore) return;
    fetchTransactions({ ...baseFilters, offset: transactions.length }, { mode: 'append' });
  };

  // Après une création ou une suppression, la liste est rechargée depuis le serveur sur la profondeur déjà
  // affichée. Une ligne créée aujourd'hui se place en tête côté serveur et décale tout d'un rang, une
  // suppression fait remonter les suivantes : aucun offset compté côté client ne survit à une mutation.
  const reloadLoaded = () => {
    const limit = Math.min(MAX_PAGE_LIMIT, Math.max(PAGE_SIZE, transactions.length));
    fetchTransactions({ ...baseFilters, limit, offset: 0 }, { mode: 'refresh' });
  };

  const countLabel = (n: number) => `${formatCount(n)} ${n === 1 ? 'transaction' : 'transactions'}`;

  // Sous le tableau et sous les cartes : « 100 sur 2 305 · Voir plus », ou « 2 305 transactions » une fois tout chargé.
  const renderTotalLine = () => {
    if (loading || total === null || transactions.length === 0) return null;
    return (
      <div className="flex items-center justify-center gap-3 px-4 py-3 border-t border-white/5 text-sm text-white/40">
        {hasMore ? (
          <>
            <span>{formatCount(transactions.length)} sur {formatCount(total)}</span>
            <span aria-hidden="true">·</span>
            <button
              type="button"
              onClick={loadMore}
              disabled={loadingMore}
              className="text-amber-400 hover:text-amber-300 font-medium transition-colors disabled:opacity-50"
            >
              {loadingMore ? 'Chargement...' : 'Voir plus'}
            </button>
          </>
        ) : (
          <span>{countLabel(total)}</span>
        )}
      </div>
    );
  };

  // Debounce recherche
  const handleSearchChange = (value: string) => {
    setSearchInput(value);
    if (searchTimeoutRef.current) clearTimeout(searchTimeoutRef.current);
    searchTimeoutRef.current = setTimeout(() => {
      setSearchQuery(value);
    }, 300);
  };

  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortDesc(!sortDesc);
    } else {
      setSortBy(column);
      setSortDesc(true);
    }
  };

  const renderSortIcon = (column: string) => {
    if (sortBy !== column) return <span className="text-white/20 ml-1">▼</span>;
    return <span className="text-amber-400 ml-1">{sortDesc ? '▼' : '▲'}</span>;
  };

  const startInlineEdit = (t: Transaction) => {
    setEditingRowId(t.id);
    setEditValues({ description: t.description, categoryId: t.categoryId, projectEnvelopeId: t.projectEnvelopeId ?? null });
  };

  const cancelInlineEdit = () => {
    setEditingRowId(null);
  };

  const saveInlineEdit = async (t: Transaction) => {
    try {
      await updateTransaction(t.id, {
        amount: t.amount,
        description: editValues.description,
        date: t.date,
        type: t.type,
        categoryId: editValues.categoryId,
        accountId: t.accountId,
      });
      // Rattachement enveloppe : appel dédié seulement si le lien a changé
      const before = t.projectEnvelopeId ?? null;
      if (editValues.projectEnvelopeId !== before) {
        await setEnvelope(t.id, editValues.projectEnvelopeId);
        queryClient.invalidateQueries({ queryKey: ['project-envelopes'] });
      }
      setEditingRowId(null);
    } catch {
      showToast('Impossible de sauvegarder la modification', 'error');
    }
  };

  const openCreateForm = () => {
    setFormError(null);
    setFormData({
      amount: 0,
      description: '',
      date: new Date().toISOString().split('T')[0],
      type: TransactionType.Expense,
      categoryId: categories[0]?.id ?? 0,
      accountId: accounts[0]?.id ?? 0,
    });
    setShowForm(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);
    try {
      await createTransaction(formData);
      setShowForm(false);
      showToast('Transaction ajoutée', 'success');
      reloadLoaded();
    } catch {
      setFormError('Erreur lors de la sauvegarde de la transaction');
      showToast('Erreur lors de la sauvegarde de la transaction', 'error');
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await deleteTransaction(id);
      setDeleteConfirm(null);
      showToast('Transaction supprimée', 'success');
      reloadLoaded();
    } catch {
      setDeleteConfirm(null);
      showToast('Impossible de supprimer la transaction', 'error');
    }
  };

  const handleToggleExceptional = async (t: Transaction) => {
    try {
      const updated = await setExceptional(t.id, !t.isExceptional);
      queryClient.invalidateQueries({ queryKey: ['summary'] });
      queryClient.invalidateQueries({ queryKey: ['category-detail'] });
      showToast(updated.isExceptional ? 'Marquée comme exceptionnelle' : 'Retirée des exceptionnelles', 'success');
    } catch {
      showToast('Impossible de modifier la transaction', 'error');
    }
  };

  const handleToggleFixed = async (t: Transaction) => {
    try {
      const updated = await setFixed(t.id, !t.isFixed);
      queryClient.invalidateQueries({ queryKey: ['monthly-report'] });
      queryClient.invalidateQueries({ queryKey: ['burndown'] });
      showToast(updated.isFixed ? 'Marquée comme charge fixe' : 'Retirée des charges fixes', 'success');
    } catch {
      showToast('Impossible de modifier la transaction', 'error');
    }
  };

  const handleToggleRefund = async (t: Transaction) => {
    try {
      const updated = await setRefund(t.id, !t.isRefund);
      queryClient.invalidateQueries({ queryKey: ['summary'] });
      queryClient.invalidateQueries({ queryKey: ['monthly-report'] });
      queryClient.invalidateQueries({ queryKey: ['category-detail'] });
      showToast(
        updated.isRefund
          ? 'Marquée comme remboursement — déduite de sa catégorie au lieu de compter en entrées'
          : 'Recomptée comme une rentrée',
        'success'
      );
    } catch {
      showToast('Impossible de modifier la transaction', 'error');
    }
  };

  return (
    <div className="space-y-6 animate-[fadeIn_0.5s_ease-out]">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-3xl font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
            Transactions {currentDashboard ? `— ${currentDashboard.name}` : ''}
          </h2>
          {total !== null && !loading && (
            <p className="text-white/40 text-sm mt-1">
              {countLabel(total)}{searchActive ? ' pour cette recherche' : period === 'all' ? '' : ' sur la période'}
            </p>
          )}
        </div>
        <button
          onClick={openCreateForm}
          className="px-5 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200"
        >
          + Ajouter
        </button>
      </div>

      {/* Filtres */}
      <div className="flex flex-wrap gap-4">
        <select
          aria-label="Période"
          value={period}
          onChange={(e) => changePeriod(e.target.value as Period)}
          disabled={searchActive}
          title={searchActive ? "La recherche porte sur tout l'historique" : undefined}
          className="px-4 py-2 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <option value="1">1 mois</option>
          <option value="3">3 mois</option>
          <option value="12">12 mois</option>
          <option value="all">Tout</option>
        </select>
        <input
          type="text"
          placeholder="Rechercher..."
          value={searchInput}
          onChange={(e) => handleSearchChange(e.target.value)}
          className="px-4 py-2 rounded-xl bg-white/5 border border-white/10 text-white placeholder-white/30 focus:outline-none focus:border-amber-500/50 min-w-[200px]"
        />
        <select
          value={filterType}
          onChange={(e) => setFilterType(e.target.value === '' ? '' : Number(e.target.value) as TransactionType)}
          className="px-4 py-2 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
        >
          <option value="">Tous les types</option>
          <option value={TransactionType.Income}>Revenus</option>
          <option value={TransactionType.Expense}>Dépenses</option>
        </select>
        <select
          value={filterCategory}
          onChange={(e) => setFilterCategory(e.target.value === '' ? '' : Number(e.target.value))}
          className="px-4 py-2 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
        >
          <option value="">Toutes les catégories</option>
          {categories.map((c) => (
            <option key={c.id} value={c.id}>{c.icon} {c.name}</option>
          ))}
        </select>
        <select
          value={filterAccount}
          onChange={(e) => setFilterAccount(e.target.value === '' ? '' : Number(e.target.value))}
          className="px-4 py-2 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
        >
          <option value="">Tous les comptes</option>
          {accounts.map((a) => (
            <option key={a.id} value={a.id}>{a.name}</option>
          ))}
        </select>
      </div>

      {/* Vue desktop : tableau */}
      <div className="hidden md:block bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 overflow-x-auto">
        {loading ? (
          <div className="p-8 text-center text-white/40">Chargement...</div>
        ) : transactions.length === 0 ? (
          <div className="p-8 text-center text-white/30">Aucune transaction</div>
        ) : (
          <table className="w-full min-w-[800px]">
            <thead>
              <tr className="border-b border-white/10">
                <th className="text-left p-4 text-white/40 font-medium text-sm cursor-pointer select-none hover:text-white/60 transition-colors" onClick={() => handleSort('date')}>
                  Date{renderSortIcon('date')}
                </th>
                <th className="text-left p-4 text-white/40 font-medium text-sm cursor-pointer select-none hover:text-white/60 transition-colors" onClick={() => handleSort('description')}>
                  Description{renderSortIcon('description')}
                </th>
                <th className="text-left p-4 text-white/40 font-medium text-sm cursor-pointer select-none hover:text-white/60 transition-colors" onClick={() => handleSort('account')}>
                  Compte{renderSortIcon('account')}
                </th>
                <th className="text-left p-4 text-white/40 font-medium text-sm cursor-pointer select-none hover:text-white/60 transition-colors" onClick={() => handleSort('category')}>
                  Catégorie{renderSortIcon('category')}
                </th>
                <th className="text-right p-4 text-white/40 font-medium text-sm cursor-pointer select-none hover:text-white/60 transition-colors" onClick={() => handleSort('amount')}>
                  Montant{renderSortIcon('amount')}
                </th>
                <th className="text-right p-4 text-white/40 font-medium text-sm">Actions</th>
              </tr>
            </thead>
            <tbody>
              {transactions.map((t) => (
                <tr key={t.id} className="border-b border-white/5 hover:bg-white/5 transition-colors">
                  <td className="p-4 text-white/60">{new Date(t.date).toLocaleDateString('fr-FR')}</td>
                  <td className="p-4 text-white max-w-[300px]">
                    {editingRowId === t.id ? (
                      <input
                        type="text"
                        value={editValues.description}
                        onChange={(e) => setEditValues({ ...editValues, description: e.target.value })}
                        className="w-full px-3 py-1.5 rounded-lg bg-white/10 border border-amber-500/50 text-white focus:outline-none"
                      />
                    ) : (
                      <>
                        <span className="block truncate" title={t.description}>{t.description || <em className="text-white/30">(sans libellé)</em>}</span>
                        {t.counterpartyName && (
                          <span
                            className="block text-white/50 text-xs truncate mt-0.5"
                            title={t.counterpartyIban ? `${t.counterpartyName} · ${t.counterpartyIban}` : t.counterpartyName}
                          >
                            {t.type === TransactionType.Income ? '↩ De ' : '↪ Vers '}{t.counterpartyName}
                            {t.counterpartyIban && <span className="text-white/30"> · {t.counterpartyIban}</span>}
                          </span>
                        )}
                        {t.isImported && (
                          <span className="ml-2 px-2 py-0.5 rounded-full text-xs font-medium bg-teal-500/20 text-teal-400 border border-teal-500/30">
                            Importée
                          </span>
                        )}
                        {t.isProvisional && (
                          <span className="ml-2 px-2 py-0.5 rounded-full text-xs font-medium bg-violet-500/20 text-violet-300 border border-dashed border-violet-500/40" title="Montant estimé, remplacé automatiquement quand le versement réel arrive">
                            Prévu
                          </span>
                        )}
                      </>
                    )}
                  </td>
                  <td className="p-4">
                    {t.bankInstitutionName ? (
                      <div>
                        <span className="block text-white/80 text-sm font-medium">{t.bankInstitutionName}</span>
                        {t.bankAccountName && (
                          <span className="block text-white/40 text-xs truncate max-w-[160px]" title={t.bankAccountName}>{t.bankAccountName}</span>
                        )}
                      </div>
                    ) : (
                      <span className="text-white/50 text-sm italic">{t.accountName}</span>
                    )}
                  </td>
                  <td className="p-4">
                    {editingRowId === t.id ? (
                      <div className="space-y-1.5">
                        <select
                          value={editValues.categoryId}
                          onChange={(e) => setEditValues({ ...editValues, categoryId: Number(e.target.value) })}
                          className="px-3 py-1.5 rounded-lg bg-white/10 border border-amber-500/50 text-white focus:outline-none"
                        >
                          {categories.map((c) => (
                            <option key={c.id} value={c.id}>{c.icon} {c.name}</option>
                          ))}
                        </select>
                        <select
                          value={editValues.projectEnvelopeId ?? ''}
                          onChange={(e) => setEditValues({ ...editValues, projectEnvelopeId: e.target.value === '' ? null : Number(e.target.value) })}
                          aria-label="Enveloppe projet"
                          className="block px-3 py-1.5 rounded-lg bg-white/10 border border-white/20 text-white text-sm focus:outline-none focus:border-amber-500/50"
                        >
                          <option value="">✉️ Aucune enveloppe</option>
                          {envelopes.map((e) => (
                            <option key={e.id} value={e.id}>{e.icon} {e.name}</option>
                          ))}
                        </select>
                      </div>
                    ) : (
                      <div className="flex flex-col items-start gap-1">
                        <span className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-white/5 text-white/70 text-sm">
                          {t.categoryIcon} {t.categoryName}
                        </span>
                        {t.projectEnvelopeName && (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-amber-500/10 text-amber-300/80 text-xs border border-amber-500/20" title={`Enveloppe : ${t.projectEnvelopeName}`}>
                            ✉️ {t.projectEnvelopeName}
                          </span>
                        )}
                      </div>
                    )}
                  </td>
                  <td className={`p-4 text-right font-semibold ${t.type === TransactionType.Income ? 'text-emerald-400' : 'text-red-400'}`}>
                    {t.type === TransactionType.Income ? '+' : '-'}{formatCurrency(t.amount)}
                  </td>
                  <td className="p-4 text-right space-x-2">
                    {editingRowId === t.id ? (
                      <>
                        <button onClick={() => saveInlineEdit(t)} className="text-emerald-400 hover:text-emerald-300 transition-colors">✓</button>
                        <button onClick={cancelInlineEdit} className="text-white/40 hover:text-red-400 transition-colors">✗</button>
                      </>
                    ) : (
                      <>
                        <button
                          aria-label="Charge fixe"
                          title={t.isFixed ? 'Charge fixe — cliquer pour retirer du bloc FIXE du bilan' : 'Marquer comme charge fixe (bloc FIXE du bilan mensuel)'}
                          onClick={() => handleToggleFixed(t)}
                          className={`transition-colors ${t.isFixed ? 'text-blue-400' : 'text-white/40 hover:text-blue-400'}`}
                        >📌</button>
                        <button
                          aria-label="Dépense exceptionnelle"
                          title="Dépense exceptionnelle"
                          onClick={() => handleToggleExceptional(t)}
                          className={`transition-colors ${t.isExceptional ? 'text-amber-400' : 'text-white/40 hover:text-amber-400'}`}
                        >⚡</button>
                        {t.type === TransactionType.Income && (
                          <button
                            aria-label="Remboursement"
                            title={t.isRefund ? 'Remboursement — cliquer pour recompter en rentrée' : 'Marquer comme remboursement (déduit de sa catégorie au lieu de compter en entrées)'}
                            onClick={() => handleToggleRefund(t)}
                            className={`transition-colors ${t.isRefund ? 'text-teal-400' : 'text-white/40 hover:text-teal-400'}`}
                          >↩️</button>
                        )}
                        <button aria-label="Édition rapide" onClick={() => startInlineEdit(t)} className="text-white/40 hover:text-amber-400 transition-colors">✏️</button>
                        {deleteConfirm === t.id ? (
                          <>
                            <button onClick={() => handleDelete(t.id)} className="text-red-400 hover:text-red-300 text-sm font-medium">Confirmer</button>
                            <button onClick={() => setDeleteConfirm(null)} className="text-white/40 hover:text-white text-sm">Annuler</button>
                          </>
                        ) : (
                          <button aria-label="Supprimer" onClick={() => setDeleteConfirm(t.id)} className="text-white/40 hover:text-red-400 transition-colors">🗑️</button>
                        )}
                      </>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {renderTotalLine()}
      </div>

      {/* Vue mobile : cartes empilées */}
      <div className="block md:hidden space-y-3">
        {loading ? (
          <div className="p-8 text-center text-white/40">Chargement...</div>
        ) : transactions.length === 0 ? (
          <div className="p-8 text-center text-white/30">Aucune transaction</div>
        ) : (
          transactions.map((t) => (
            <div key={t.id} className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-4">
              <div className="flex items-start justify-between gap-3">
                <div className="flex-1 min-w-0">
                  <p className="text-white font-medium truncate">{t.description || <em className="text-white/30">(sans libellé)</em>}</p>
                  {t.counterpartyName && (
                    <p className="text-white/50 text-xs mt-0.5 truncate">
                      {t.type === TransactionType.Income ? '↩ De ' : '↪ Vers '}{t.counterpartyName}
                    </p>
                  )}
                  <p className="text-white/40 text-xs mt-0.5">{new Date(t.date).toLocaleDateString('fr-FR')}</p>
                </div>
                <span className={`text-lg font-bold flex-shrink-0 ${t.type === TransactionType.Income ? 'text-emerald-400' : 'text-red-400'}`}>
                  {t.type === TransactionType.Income ? '+' : '-'}{formatCurrency(t.amount)}
                </span>
              </div>
              <div className="flex items-center justify-between mt-3">
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-white/5 text-white/70 text-xs border border-white/10">
                    {t.categoryIcon} {t.categoryName}
                  </span>
                  {t.projectEnvelopeName && (
                    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-amber-500/10 text-amber-300/80 text-xs border border-amber-500/20" title={`Enveloppe : ${t.projectEnvelopeName}`}>
                      ✉️ {t.projectEnvelopeName}
                    </span>
                  )}
                  {t.isImported && (
                    <span className="px-2 py-0.5 rounded-full text-xs font-medium bg-teal-500/20 text-teal-400 border border-teal-500/30">
                      Importée
                    </span>
                  )}
                  {t.isProvisional && (
                    <span className="px-2 py-0.5 rounded-full text-xs font-medium bg-violet-500/20 text-violet-300 border border-dashed border-violet-500/40" title="Montant estimé, remplacé automatiquement quand le versement réel arrive">
                      Prévu
                    </span>
                  )}
                </div>
                <div className="flex items-center gap-3">
                  {deleteConfirm === t.id ? (
                    <>
                      <button onClick={() => handleDelete(t.id)} className="text-red-400 hover:text-red-300 text-sm font-medium">Confirmer</button>
                      <button onClick={() => setDeleteConfirm(null)} className="text-white/40 hover:text-white text-sm">Annuler</button>
                    </>
                  ) : (
                    <>
                      <button
                        aria-label="Charge fixe"
                        title={t.isFixed ? 'Charge fixe — cliquer pour retirer' : 'Marquer comme charge fixe'}
                        onClick={() => handleToggleFixed(t)}
                        className={`transition-colors ${t.isFixed ? 'text-blue-400' : 'text-white/40 hover:text-blue-400'}`}
                      >📌</button>
                      <button
                        aria-label="Dépense exceptionnelle"
                        title="Dépense exceptionnelle"
                        onClick={() => handleToggleExceptional(t)}
                        className={`transition-colors ${t.isExceptional ? 'text-amber-400' : 'text-white/40 hover:text-amber-400'}`}
                      >⚡</button>
                      {t.type === TransactionType.Income && (
                        <button
                          aria-label="Remboursement"
                          title={t.isRefund ? 'Remboursement — cliquer pour recompter en rentrée' : 'Marquer comme remboursement'}
                          onClick={() => handleToggleRefund(t)}
                          className={`transition-colors ${t.isRefund ? 'text-teal-400' : 'text-white/40 hover:text-teal-400'}`}
                        >↩️</button>
                      )}
                      <button aria-label="Supprimer" onClick={() => setDeleteConfirm(t.id)} className="text-white/40 hover:text-red-400 transition-colors">🗑️</button>
                    </>
                  )}
                </div>
              </div>
            </div>
          ))
        )}
        {renderTotalLine()}
      </div>

      {/* Modal formulaire */}
      {showForm && (
        <div role="dialog" aria-modal="true" className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-end md:items-center justify-center" onClick={() => setShowForm(false)}>
          <div className="bg-[#1a1a3e] rounded-t-2xl md:rounded-2xl border border-white/10 p-6 md:p-8 w-full md:max-w-md max-h-[90vh] overflow-y-auto" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-white mb-6">
              Nouvelle transaction
            </h3>
            <form onSubmit={handleSubmit} className="space-y-4">
              {formError && (
                <div className="mb-4 p-3 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-sm">
                  {formError}
                </div>
              )}
              <div>
                <label className="block text-sm text-white/60 mb-1">Compte</label>
                <select
                  value={formData.accountId}
                  onChange={(e) => setFormData({ ...formData, accountId: Number(e.target.value) })}
                  required
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                >
                  <option value={0} disabled>Sélectionner...</option>
                  {accounts.map((a) => (
                    <option key={a.id} value={a.id}>{a.name}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Type</label>
                <select
                  value={formData.type}
                  onChange={(e) => setFormData({ ...formData, type: Number(e.target.value) as TransactionType })}
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                >
                  <option value={TransactionType.Expense}>Dépense</option>
                  <option value={TransactionType.Income}>Revenu</option>
                </select>
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Montant (€)</label>
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={formData.amount || ''}
                  onChange={(e) => setFormData({ ...formData, amount: parseFloat(e.target.value) || 0 })}
                  required
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Description</label>
                <input
                  type="text"
                  value={formData.description}
                  onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                  required
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Date</label>
                <input
                  type="date"
                  value={formData.date}
                  onChange={(e) => setFormData({ ...formData, date: e.target.value })}
                  required
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Catégorie</label>
                <select
                  value={formData.categoryId}
                  onChange={(e) => setFormData({ ...formData, categoryId: Number(e.target.value) })}
                  required
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                >
                  <option value={0} disabled>Sélectionner...</option>
                  {categories.map((c) => (
                    <option key={c.id} value={c.id}>{c.icon} {c.name}</option>
                  ))}
                </select>
              </div>
              <div className="flex gap-3 pt-2">
                <button type="submit" className="flex-1 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all">
                  Ajouter
                </button>
                <button type="button" onClick={() => setShowForm(false)} className="px-6 py-2.5 rounded-xl border border-white/10 text-white/60 hover:text-white hover:bg-white/5 transition-all">
                  Annuler
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};

export default Transactions;
