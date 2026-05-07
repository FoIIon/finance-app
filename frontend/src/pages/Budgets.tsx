import { useState, useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useDashboards } from '../hooks/useDashboards';
import { useBudgetsQuery } from '../hooks/queries';
import { budgetsApi } from '../api/budgets';
import { categoriesApi } from '../api/categories';
import type { Category } from '../types/category';
import { formatCurrency } from '../utils/format';
import { useToast } from '../context/ToastContext';

const Budgets = () => {
  const { currentDashboard } = useDashboards();
  const { data: budgets, isLoading } = useBudgetsQuery(currentDashboard?.id);
  const [categories, setCategories] = useState<Category[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState({ categoryId: 0, amount: 0, period: 'Monthly' as 'Monthly' | 'Yearly' });
  const [deleteConfirm, setDeleteConfirm] = useState<number | null>(null);
  const qc = useQueryClient();
  const { showToast } = useToast();

  useEffect(() => {
    categoriesApi.getAll().then(r => setCategories(r.data));
  }, []);

  const usedCategoryIds = new Set(budgets?.map(b => b.categoryId) ?? []);
  const availableCategories = categories.filter(c => c.id !== 10 && (!usedCategoryIds.has(c.id) || c.id === editingId));

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!currentDashboard) return;
    try {
      if (editingId) {
        await budgetsApi.update(editingId, { amount: form.amount, period: form.period });
        showToast('Budget mis à jour', 'success');
      } else {
        await budgetsApi.create({
          dashboardId: currentDashboard.id,
          categoryId: form.categoryId,
          amount: form.amount,
          period: form.period,
        });
        showToast('Budget créé', 'success');
      }
      qc.invalidateQueries({ queryKey: ['budgets'] });
      qc.invalidateQueries({ queryKey: ['budgets-progress'] });
      setShowForm(false);
      setEditingId(null);
      setForm({ categoryId: 0, amount: 0, period: 'Monthly' });
    } catch {
      showToast('Erreur lors de la sauvegarde', 'error');
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await budgetsApi.delete(id);
      qc.invalidateQueries({ queryKey: ['budgets'] });
      qc.invalidateQueries({ queryKey: ['budgets-progress'] });
      setDeleteConfirm(null);
      showToast('Budget supprimé', 'success');
    } catch {
      showToast('Erreur lors de la suppression', 'error');
    }
  };

  const startEdit = (id: number, categoryId: number, amount: number, period: 'Monthly' | 'Yearly') => {
    setEditingId(id);
    setForm({ categoryId, amount, period });
    setShowForm(true);
  };

  const startCreate = () => {
    setEditingId(null);
    setForm({ categoryId: availableCategories[0]?.id ?? 0, amount: 0, period: 'Monthly' });
    setShowForm(true);
  };

  return (
    <div className="space-y-6 animate-[fadeIn_0.15s_ease-out]">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl md:text-3xl font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
            Budgets
          </h2>
          <p className="text-white/40 text-sm mt-1">
            Définis combien tu veux dépenser par catégorie chaque mois
          </p>
        </div>
        <button
          onClick={startCreate}
          disabled={availableCategories.length === 0}
          className="px-4 py-2 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white text-sm font-semibold hover:from-amber-600 hover:to-orange-700 transition-all disabled:opacity-50 disabled:cursor-not-allowed"
        >
          + Nouveau budget
        </button>
      </div>

      <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 overflow-hidden">
        {isLoading ? (
          <div className="p-8 text-center text-white/40">Chargement...</div>
        ) : !budgets || budgets.length === 0 ? (
          <div className="p-8 text-center">
            <p className="text-white/40 text-sm">Aucun budget défini.</p>
            <p className="text-white/30 text-xs mt-1">Crée ton premier budget pour suivre tes dépenses.</p>
          </div>
        ) : (
          <table className="w-full">
            <thead>
              <tr className="border-b border-white/10">
                <th className="text-left p-4 text-white/40 text-xs font-medium">Catégorie</th>
                <th className="text-right p-4 text-white/40 text-xs font-medium">Montant</th>
                <th className="text-right p-4 text-white/40 text-xs font-medium hidden sm:table-cell">Période</th>
                <th className="text-right p-4 text-white/40 text-xs font-medium">Actions</th>
              </tr>
            </thead>
            <tbody>
              {budgets.map((b) => (
                <tr key={b.id} className="border-b border-white/5 last:border-0 hover:bg-white/5 transition-colors">
                  <td className="p-4">
                    <div className="flex items-center gap-2">
                      <span aria-hidden="true">{b.categoryIcon}</span>
                      <span className="text-white text-sm">{b.categoryName}</span>
                    </div>
                  </td>
                  <td className="p-4 text-right text-white text-sm font-semibold">
                    {formatCurrency(b.amount)}
                  </td>
                  <td className="p-4 text-right text-white/50 text-sm hidden sm:table-cell">
                    {b.period === 'Monthly' ? 'Mensuel' : 'Annuel'}
                  </td>
                  <td className="p-4 text-right space-x-2">
                    <button
                      onClick={() => startEdit(b.id, b.categoryId, b.amount, b.period)}
                      className="text-white/40 hover:text-amber-400 transition-colors"
                      aria-label="Modifier"
                    >
                      ✏️
                    </button>
                    {deleteConfirm === b.id ? (
                      <>
                        <button onClick={() => handleDelete(b.id)} className="text-red-400 hover:text-red-300 text-sm font-medium">Confirmer</button>
                        <button onClick={() => setDeleteConfirm(null)} className="text-white/40 hover:text-white text-sm">Annuler</button>
                      </>
                    ) : (
                      <button
                        onClick={() => setDeleteConfirm(b.id)}
                        className="text-white/40 hover:text-red-400 transition-colors"
                        aria-label="Supprimer"
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

      {showForm && (
        <div role="dialog" aria-modal="true" className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4" onClick={() => setShowForm(false)}>
          <div className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-6 w-full max-w-md" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-white mb-4">
              {editingId ? 'Modifier le budget' : 'Nouveau budget'}
            </h3>
            <form onSubmit={handleSubmit} className="space-y-4">
              {!editingId && (
                <div>
                  <label className="block text-sm text-white/60 mb-1">Catégorie</label>
                  <select
                    required
                    value={form.categoryId || ''}
                    onChange={(e) => setForm({ ...form, categoryId: Number(e.target.value) })}
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                  >
                    <option value="" disabled>Sélectionner...</option>
                    {availableCategories.map(c => (
                      <option key={c.id} value={c.id}>{c.icon} {c.name}</option>
                    ))}
                  </select>
                </div>
              )}
              <div>
                <label className="block text-sm text-white/60 mb-1">Montant (€)</label>
                <input
                  type="number"
                  required
                  step="0.01"
                  min="0.01"
                  value={form.amount || ''}
                  onChange={(e) => setForm({ ...form, amount: parseFloat(e.target.value) || 0 })}
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Période</label>
                <select
                  value={form.period}
                  onChange={(e) => setForm({ ...form, period: e.target.value as 'Monthly' | 'Yearly' })}
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                >
                  <option value="Monthly">Mensuel</option>
                  <option value="Yearly">Annuel</option>
                </select>
              </div>
              <div className="flex gap-3 pt-2">
                <button type="submit" className="flex-1 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold">
                  {editingId ? 'Enregistrer' : 'Créer'}
                </button>
                <button type="button" onClick={() => { setShowForm(false); setEditingId(null); }} className="px-6 py-2.5 rounded-xl border border-white/10 text-white/60 hover:text-white">
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

export default Budgets;
