import { useState, useEffect } from 'react';
import { useRecurringTransactions } from '../hooks/useRecurringTransactions';
import { useDashboards } from '../hooks/useDashboards';
import { categoriesApi } from '../api/categories';
import { accountsApi } from '../api/accounts';
import type { Category } from '../types/category';
import type { Account } from '../types/dashboard';
import type { CreateRecurringTransaction, RecurringFrequency, RecurringType } from '../types/recurringTransaction';
import { formatCurrency } from '../utils/format';

const FREQUENCY_LABELS: Record<RecurringFrequency, string> = {
  Weekly: 'Hebdo',
  Monthly: 'Mensuelle',
  Yearly: 'Annuelle',
};

const FREQUENCY_COLORS: Record<RecurringFrequency, string> = {
  Weekly: 'bg-blue-500/20 text-blue-400 border-blue-500/30',
  Monthly: 'bg-amber-500/20 text-amber-400 border-amber-500/30',
  Yearly: 'bg-purple-500/20 text-purple-400 border-purple-500/30',
};

const emptyForm = (): CreateRecurringTransaction => ({
  dashboardId: 0,
  description: '',
  amount: 0,
  type: 'Expense',
  frequency: 'Monthly',
  dayOfMonth: null,
  startDate: new Date().toISOString().split('T')[0],
  endDate: null,
  accountId: null,
  categoryId: null,
  provisionAtMonthStart: false,
});

const RecurringTransactions = () => {
  const { recurringTransactions, loading, error, createRecurring, deleteRecurring, toggleActive } =
    useRecurringTransactions();
  const { currentDashboard } = useDashboards();
  const [categories, setCategories] = useState<Category[]>([]);
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [formData, setFormData] = useState<CreateRecurringTransaction>(emptyForm());
  const [formError, setFormError] = useState<string | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<number | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    categoriesApi.getAll().then((res) => setCategories(res.data));
    accountsApi.getAll().then((res) => setAccounts(res.data));
  }, []);

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setShowForm(false);
    };
    if (showForm) {
      document.addEventListener('keydown', handleEscape);
      return () => document.removeEventListener('keydown', handleEscape);
    }
  }, [showForm]);

  const openCreateForm = () => {
    setFormError(null);
    setFormData({
      ...emptyForm(),
      categoryId: categories[0]?.id ?? null,
      accountId: accounts[0]?.id ?? null,
    });
    setShowForm(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);
    setSubmitting(true);
    try {
      await createRecurring(formData);
      setShowForm(false);
    } catch {
      setFormError('Erreur lors de la sauvegarde');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await deleteRecurring(id);
      setDeleteConfirm(null);
    } catch {
      setDeleteConfirm(null);
    }
  };

  const handleToggle = async (id: number, currentState: boolean) => {
    try {
      await toggleActive(id, !currentState);
    } catch {
      // silencieux
    }
  };

  return (
    <div className="space-y-6 animate-[fadeIn_0.5s_ease-out]">
      <div className="flex items-center justify-between">
        <h2
          className="text-3xl font-bold text-white"
          style={{ fontFamily: "'Space Grotesk', sans-serif" }}
        >
          Récurrentes {currentDashboard ? `— ${currentDashboard.name}` : ''}
        </h2>
        <button
          onClick={openCreateForm}
          className="px-5 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200"
        >
          + Ajouter
        </button>
      </div>

      {error && (
        <div className="p-4 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-sm">
          {error}
        </div>
      )}

      <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 overflow-x-auto">
        {loading ? (
          <div className="p-8 text-center text-white/40">Chargement...</div>
        ) : recurringTransactions.length === 0 ? (
          <div className="p-8 text-center text-white/30">
            Aucune transaction récurrente — cliquez sur "+ Ajouter" pour commencer.
          </div>
        ) : (
          <table className="w-full min-w-[700px]">
            <thead>
              <tr className="border-b border-white/10">
                <th className="text-left p-4 text-white/40 font-medium text-sm">Description</th>
                <th className="text-left p-4 text-white/40 font-medium text-sm">Catégorie</th>
                <th className="text-left p-4 text-white/40 font-medium text-sm">Fréquence</th>
                <th className="text-left p-4 text-white/40 font-medium text-sm">Début</th>
                <th className="text-right p-4 text-white/40 font-medium text-sm">Montant</th>
                <th className="text-right p-4 text-white/40 font-medium text-sm">Actif</th>
                <th className="text-right p-4 text-white/40 font-medium text-sm">Actions</th>
              </tr>
            </thead>
            <tbody>
              {recurringTransactions.map((r) => (
                <tr
                  key={r.id}
                  className={`border-b border-white/5 transition-colors ${
                    r.isActive ? 'hover:bg-white/5' : 'opacity-50 hover:bg-white/3'
                  }`}
                >
                  <td className="p-4 text-white max-w-[220px]">
                    <span className="block truncate font-medium" title={r.description}>
                      {r.description}
                    </span>
                    {r.accountName && (
                      <span className="block text-xs text-white/40 mt-0.5">{r.accountName}</span>
                    )}
                  </td>
                  <td className="p-4">
                    {r.categoryName ? (
                      <span className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-white/5 text-white/70 text-sm">
                        {r.categoryIcon} {r.categoryName}
                      </span>
                    ) : (
                      <span className="text-white/30 text-sm italic">—</span>
                    )}
                  </td>
                  <td className="p-4">
                    <span
                      className={`inline-flex items-center px-2.5 py-1 rounded-full text-xs font-medium border ${FREQUENCY_COLORS[r.frequency]}`}
                    >
                      {FREQUENCY_LABELS[r.frequency]}
                      {r.frequency === 'Monthly' && r.dayOfMonth ? ` (j.${r.dayOfMonth})` : ''}
                    </span>
                  </td>
                  <td className="p-4 text-white/60 text-sm">
                    {r.startDate}
                    {r.endDate && (
                      <span className="block text-white/30 text-xs">jusqu'au {r.endDate}</span>
                    )}
                  </td>
                  <td
                    className={`p-4 text-right font-semibold ${
                      r.type === 'Income' ? 'text-emerald-400' : 'text-red-400'
                    }`}
                  >
                    {r.type === 'Income' ? '+' : '-'}
                    {formatCurrency(r.amount)}
                  </td>
                  <td className="p-4 text-right">
                    <button
                      onClick={() => handleToggle(r.id, r.isActive)}
                      title={r.isActive ? 'Désactiver' : 'Réactiver'}
                      className={`w-10 h-5 rounded-full transition-colors relative ${
                        r.isActive ? 'bg-emerald-500/60' : 'bg-white/10'
                      }`}
                    >
                      <span
                        className={`absolute top-0.5 w-4 h-4 rounded-full bg-white shadow transition-all ${
                          r.isActive ? 'left-5' : 'left-0.5'
                        }`}
                      />
                    </button>
                  </td>
                  <td className="p-4 text-right">
                    {deleteConfirm === r.id ? (
                      <span className="space-x-2">
                        <button
                          onClick={() => handleDelete(r.id)}
                          className="text-red-400 hover:text-red-300 text-sm font-medium"
                        >
                          Confirmer
                        </button>
                        <button
                          onClick={() => setDeleteConfirm(null)}
                          className="text-white/40 hover:text-white text-sm"
                        >
                          Annuler
                        </button>
                      </span>
                    ) : (
                      <button
                        aria-label="Supprimer"
                        onClick={() => setDeleteConfirm(r.id)}
                        className="text-white/40 hover:text-red-400 transition-colors"
                      >
                        🗑️
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Modal création */}
      {showForm && (
        <div
          role="dialog"
          aria-modal="true"
          className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center"
          onClick={() => setShowForm(false)}
        >
          <div
            className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-8 w-full max-w-lg overflow-y-auto max-h-[90vh]"
            onClick={(e) => e.stopPropagation()}
          >
            <h3 className="text-xl font-bold text-white mb-6">Nouvelle transaction récurrente</h3>
            <form onSubmit={handleSubmit} className="space-y-4">
              {formError && (
                <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-sm">
                  {formError}
                </div>
              )}

              <div>
                <label className="block text-sm text-white/60 mb-1">Description</label>
                <input
                  type="text"
                  required
                  value={formData.description}
                  onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                  placeholder="Loyer, Crèche, Netflix..."
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                />
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm text-white/60 mb-1">Type</label>
                  <select
                    value={formData.type}
                    onChange={(e) => setFormData({ ...formData, type: e.target.value as RecurringType })}
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                  >
                    <option value="Expense">Dépense</option>
                    <option value="Income">Revenu</option>
                  </select>
                </div>
                <div>
                  <label className="block text-sm text-white/60 mb-1">Montant (€)</label>
                  <input
                    type="number"
                    step="0.01"
                    min="0.01"
                    required
                    value={formData.amount || ''}
                    onChange={(e) => setFormData({ ...formData, amount: parseFloat(e.target.value) || 0 })}
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                  />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm text-white/60 mb-1">Fréquence</label>
                  <select
                    value={formData.frequency}
                    onChange={(e) =>
                      setFormData({ ...formData, frequency: e.target.value as RecurringFrequency })
                    }
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                  >
                    <option value="Monthly">Mensuelle</option>
                    <option value="Weekly">Hebdomadaire</option>
                    <option value="Yearly">Annuelle</option>
                  </select>
                </div>
                {formData.frequency === 'Monthly' && (
                  <div>
                    <label className="block text-sm text-white/60 mb-1">Jour du mois</label>
                    <input
                      type="number"
                      min="1"
                      max="31"
                      value={formData.dayOfMonth ?? ''}
                      onChange={(e) =>
                        setFormData({
                          ...formData,
                          dayOfMonth: e.target.value ? parseInt(e.target.value) : null,
                        })
                      }
                      placeholder="ex: 1, 15, 28..."
                      className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                    />
                  </div>
                )}
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm text-white/60 mb-1">Date de début</label>
                  <input
                    type="date"
                    required
                    value={formData.startDate}
                    onChange={(e) => setFormData({ ...formData, startDate: e.target.value })}
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                  />
                </div>
                <div>
                  <label className="block text-sm text-white/60 mb-1">Date de fin (optionnel)</label>
                  <input
                    type="date"
                    value={formData.endDate ?? ''}
                    onChange={(e) =>
                      setFormData({ ...formData, endDate: e.target.value || null })
                    }
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                  />
                </div>
              </div>

              <div>
                <label className="block text-sm text-white/60 mb-1">Catégorie</label>
                <select
                  value={formData.categoryId ?? ''}
                  onChange={(e) =>
                    setFormData({
                      ...formData,
                      categoryId: e.target.value ? Number(e.target.value) : null,
                    })
                  }
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                >
                  <option value="">Aucune catégorie</option>
                  {categories.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.icon} {c.name}
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block text-sm text-white/60 mb-1">Compte (optionnel)</label>
                <select
                  value={formData.accountId ?? ''}
                  onChange={(e) =>
                    setFormData({
                      ...formData,
                      accountId: e.target.value ? Number(e.target.value) : null,
                    })
                  }
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                >
                  <option value="">Aucun compte</option>
                  {accounts.map((a) => (
                    <option key={a.id} value={a.id}>
                      {a.name}
                    </option>
                  ))}
                </select>
              </div>

              <label className="flex items-start gap-3 p-3 rounded-xl bg-violet-500/10 border border-violet-500/20 cursor-pointer">
                <input
                  type="checkbox"
                  checked={formData.provisionAtMonthStart ?? false}
                  onChange={(e) => setFormData({ ...formData, provisionAtMonthStart: e.target.checked })}
                  className="mt-0.5 accent-violet-500"
                />
                <span className="text-sm text-white/70">
                  <span className="font-medium text-violet-300">Provisionner en début de mois</span>
                  <br />
                  Crée une écriture « prévue » le 1er du mois (moyenne des derniers montants réels),
                  remplacée automatiquement quand le versement arrive. Idéal pour un salaire versé en fin de mois.
                  Nécessite une catégorie.
                </span>
              </label>

              <div className="flex gap-3 pt-2">
                <button
                  type="submit"
                  disabled={submitting}
                  className="flex-1 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all disabled:opacity-50"
                >
                  {submitting ? 'Enregistrement...' : 'Ajouter'}
                </button>
                <button
                  type="button"
                  onClick={() => setShowForm(false)}
                  className="px-6 py-2.5 rounded-xl border border-white/10 text-white/60 hover:text-white hover:bg-white/5 transition-all"
                >
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

export default RecurringTransactions;
