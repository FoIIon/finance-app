import { useState, useEffect, useRef } from 'react';
import { useTransactions } from '../hooks/useTransactions';
import { useDashboards } from '../hooks/useDashboards';
import { categoriesApi } from '../api/categories';
import { accountsApi } from '../api/accounts';
import type { Category } from '../types/category';
import { TransactionType } from '../types/transaction';
import type { CreateTransaction, Transaction, TransactionFilters } from '../types/transaction';
import type { Account } from '../types/dashboard';
import { formatCurrency } from '../utils/format';

const Transactions = () => {
  const { transactions, loading, fetchTransactions, createTransaction, updateTransaction, deleteTransaction } = useTransactions();
  const { currentDashboard } = useDashboards();
  const [categories, setCategories] = useState<Category[]>([]);
  const [accounts, setAccounts] = useState<Account[]>([]);
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

  // Tri
  const [sortBy, setSortBy] = useState('date');
  const [sortDesc, setSortDesc] = useState(true);

  // Édition inline
  const [editingRowId, setEditingRowId] = useState<number | null>(null);
  const [editValues, setEditValues] = useState({ description: '', categoryId: 0 });

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
    categoriesApi.getAll().then((res) => setCategories(res.data));
    accountsApi.getAll().then((res) => setAccounts(res.data));
  }, []);

  useEffect(() => {
    const filters: TransactionFilters = { sortBy, sortDesc };
    if (filterType !== '') filters.type = filterType;
    if (filterCategory !== '') filters.categoryId = filterCategory;
    if (filterAccount !== '') filters.accountId = filterAccount;
    if (searchQuery) filters.search = searchQuery;
    fetchTransactions(filters);
  }, [filterType, filterCategory, filterAccount, searchQuery, sortBy, sortDesc, fetchTransactions]);

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
    setEditValues({ description: t.description, categoryId: t.categoryId });
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
      setEditingRowId(null);
    } catch {
      // Erreur silencieuse, la transaction reste en mode édition
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
    } catch {
      setFormError('Erreur lors de la sauvegarde de la transaction');
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await deleteTransaction(id);
      setDeleteConfirm(null);
    } catch {
      setDeleteConfirm(null);
    }
  };

  return (
    <div className="space-y-6 animate-[fadeIn_0.5s_ease-out]">
      <div className="flex items-center justify-between">
        <h2 className="text-3xl font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
          Transactions {currentDashboard ? `— ${currentDashboard.name}` : ''}
        </h2>
        <button
          onClick={openCreateForm}
          className="px-5 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200"
        >
          + Ajouter
        </button>
      </div>

      {/* Filtres */}
      <div className="flex flex-wrap gap-4">
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

      {/* Tableau */}
      <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 overflow-x-auto">
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
                  Tiers{renderSortIcon('account')}
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
                        <span className="block truncate" title={t.description}>{t.description}</span>
                        {t.isImported && (
                          <span className="ml-2 px-2 py-0.5 rounded-full text-xs font-medium bg-teal-500/20 text-teal-400 border border-teal-500/30">
                            Importée
                          </span>
                        )}
                      </>
                    )}
                  </td>
                  <td className="p-4 text-white/50 text-sm">
                    {t.counterpartyName ? t.counterpartyName : <span className="italic">{t.accountName}</span>}
                  </td>
                  <td className="p-4">
                    {editingRowId === t.id ? (
                      <select
                        value={editValues.categoryId}
                        onChange={(e) => setEditValues({ ...editValues, categoryId: Number(e.target.value) })}
                        className="px-3 py-1.5 rounded-lg bg-white/10 border border-amber-500/50 text-white focus:outline-none"
                      >
                        {categories.map((c) => (
                          <option key={c.id} value={c.id}>{c.icon} {c.name}</option>
                        ))}
                      </select>
                    ) : (
                      <span className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-white/5 text-white/70 text-sm">
                        {t.categoryIcon} {t.categoryName}
                      </span>
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
      </div>

      {/* Modal formulaire */}
      {showForm && (
        <div role="dialog" aria-modal="true" className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center" onClick={() => setShowForm(false)}>
          <div className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-8 w-full max-w-md" onClick={(e) => e.stopPropagation()}>
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
