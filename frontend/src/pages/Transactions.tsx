import { useState, useEffect } from 'react';
import { useTransactions } from '../hooks/useTransactions';
import { categoriesApi } from '../api/categories';
import type { Category } from '../types/category';
import { TransactionType } from '../types/transaction';
import type { CreateTransaction, UpdateTransaction, Transaction, TransactionFilters } from '../types/transaction';
import { formatCurrency } from '../utils/format';

const Transactions = () => {
  const { transactions, loading, fetchTransactions, createTransaction, updateTransaction, deleteTransaction } = useTransactions();
  const [categories, setCategories] = useState<Category[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [editingTransaction, setEditingTransaction] = useState<Transaction | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<number | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  // Filtres
  const [filterType, setFilterType] = useState<TransactionType | ''>('');
  const [filterCategory, setFilterCategory] = useState<number | ''>('');

  // Formulaire
  const [formData, setFormData] = useState<CreateTransaction>({
    amount: 0,
    description: '',
    date: new Date().toISOString().split('T')[0],
    type: TransactionType.Expense,
    categoryId: 0,
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
  }, []);

  useEffect(() => {
    const filters: TransactionFilters = {};
    if (filterType !== '') filters.type = filterType;
    if (filterCategory !== '') filters.categoryId = filterCategory;
    fetchTransactions(filters);
  }, [filterType, filterCategory, fetchTransactions]);

  const openCreateForm = () => {
    setEditingTransaction(null);
    setFormError(null);
    setFormData({
      amount: 0,
      description: '',
      date: new Date().toISOString().split('T')[0],
      type: TransactionType.Expense,
      categoryId: categories[0]?.id ?? 0,
    });
    setShowForm(true);
  };

  const openEditForm = (t: Transaction) => {
    setEditingTransaction(t);
    setFormError(null);
    setFormData({
      amount: t.amount,
      description: t.description,
      date: t.date.split('T')[0],
      type: t.type,
      categoryId: t.categoryId,
    });
    setShowForm(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);
    try {
      if (editingTransaction) {
        await updateTransaction(editingTransaction.id, formData as UpdateTransaction);
      } else {
        await createTransaction(formData);
      }
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
          Transactions
        </h2>
        <button
          onClick={openCreateForm}
          className="px-5 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200"
        >
          + Ajouter
        </button>
      </div>

      {/* Filtres */}
      <div className="flex gap-4">
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
      </div>

      {/* Tableau */}
      <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 overflow-hidden">
        {loading ? (
          <div className="p-8 text-center text-white/40">Chargement...</div>
        ) : transactions.length === 0 ? (
          <div className="p-8 text-center text-white/30">Aucune transaction</div>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="border-b border-white/10">
                <th className="text-left p-4 text-white/40 font-medium text-sm">Date</th>
                <th className="text-left p-4 text-white/40 font-medium text-sm">Description</th>
                <th className="text-left p-4 text-white/40 font-medium text-sm">Catégorie</th>
                <th className="text-right p-4 text-white/40 font-medium text-sm">Montant</th>
                <th className="text-right p-4 text-white/40 font-medium text-sm">Actions</th>
              </tr>
            </thead>
            <tbody>
              {transactions.map((t) => (
                <tr key={t.id} className="border-b border-white/5 hover:bg-white/5 transition-colors">
                  <td className="p-4 text-white/60">{new Date(t.date).toLocaleDateString('fr-FR')}</td>
                  <td className="p-4 text-white">{t.description}</td>
                  <td className="p-4">
                    <span className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-white/5 text-white/70 text-sm">
                      {t.categoryIcon} {t.categoryName}
                    </span>
                  </td>
                  <td className={`p-4 text-right font-semibold ${t.type === TransactionType.Income ? 'text-emerald-400' : 'text-red-400'}`}>
                    {t.type === TransactionType.Income ? '+' : '-'}{formatCurrency(t.amount)}
                  </td>
                  <td className="p-4 text-right space-x-2">
                    <button aria-label="Modifier" onClick={() => openEditForm(t)} className="text-white/40 hover:text-amber-400 transition-colors">✏️</button>
                    {deleteConfirm === t.id ? (
                      <>
                        <button onClick={() => handleDelete(t.id)} className="text-red-400 hover:text-red-300 text-sm font-medium">Confirmer</button>
                        <button onClick={() => setDeleteConfirm(null)} className="text-white/40 hover:text-white text-sm">Annuler</button>
                      </>
                    ) : (
                      <button aria-label="Supprimer" onClick={() => setDeleteConfirm(t.id)} className="text-white/40 hover:text-red-400 transition-colors">🗑️</button>
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
              {editingTransaction ? 'Modifier la transaction' : 'Nouvelle transaction'}
            </h3>
            <form onSubmit={handleSubmit} className="space-y-4">
              {formError && (
                <div className="mb-4 p-3 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-sm">
                  {formError}
                </div>
              )}
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
                  {editingTransaction ? 'Modifier' : 'Ajouter'}
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
