import { useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useDashboards } from '../../hooks/useDashboards';
import { SavingsGoalsList } from '../../components/dashboard/SavingsGoalsList';
import { savingsGoalsApi } from '../../api/savingsGoals';
import { categoriesApi } from '../../api/categories';
import type { Category } from '../../types/category';
import type { CreateSavingsGoal } from '../../types/savingsGoal';

const DashboardProjects = () => {
  const { currentDashboard } = useDashboards();
  const [showForm, setShowForm] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);
  const [form, setForm] = useState<Omit<CreateSavingsGoal, 'dashboardId'>>({
    name: '',
    icon: '🎯',
    targetAmount: 0,
    targetDate: new Date(new Date().getFullYear(), 11, 31).toISOString().split('T')[0],
    categoryId: undefined,
  });
  const qc = useQueryClient();

  useEffect(() => {
    categoriesApi.getAll().then(r => setCategories(r.data));
  }, []);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!currentDashboard) return;
    await savingsGoalsApi.create({
      ...form,
      dashboardId: currentDashboard.id,
      targetDate: new Date(form.targetDate).toISOString(),
    });
    qc.invalidateQueries({ queryKey: ['savings-goals-progress'] });
    setShowForm(false);
    setForm({ name: '', icon: '🎯', targetAmount: 0, targetDate: new Date(new Date().getFullYear(), 11, 31).toISOString().split('T')[0], categoryId: undefined });
  };

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-lg font-semibold text-white">Objectifs d'épargne</h3>
          <p className="text-white/40 text-xs">Vacances, projets maison, fonds urgence…</p>
        </div>
        <button
          onClick={() => setShowForm(true)}
          className="px-4 py-2 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white text-sm font-semibold hover:from-amber-600 hover:to-orange-700 transition-all"
        >
          + Nouveau projet
        </button>
      </div>

      <SavingsGoalsList />

      {showForm && (
        <div role="dialog" aria-modal="true" className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4" onClick={() => setShowForm(false)}>
          <div className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-6 w-full max-w-md" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-white mb-4">Nouveau projet</h3>
            <form onSubmit={handleCreate} className="space-y-4">
              <div>
                <label className="block text-sm text-white/60 mb-1">Nom</label>
                <input
                  type="text"
                  required
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  placeholder="Ex: Vacances été 2027"
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Icône (emoji)</label>
                <input
                  type="text"
                  value={form.icon}
                  onChange={(e) => setForm({ ...form, icon: e.target.value })}
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Objectif (€)</label>
                <input
                  type="number"
                  required
                  step="0.01"
                  min="1"
                  value={form.targetAmount || ''}
                  onChange={(e) => setForm({ ...form, targetAmount: parseFloat(e.target.value) || 0 })}
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Date cible</label>
                <input
                  type="date"
                  required
                  value={form.targetDate}
                  onChange={(e) => setForm({ ...form, targetDate: e.target.value })}
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Catégorie liée (optionnel)</label>
                <select
                  value={form.categoryId ?? 0}
                  onChange={(e) => setForm({ ...form, categoryId: Number(e.target.value) || undefined })}
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                >
                  <option value={0}>— Aucune (montant saisi à la main) —</option>
                  {categories.map(c => (
                    <option key={c.id} value={c.id}>{c.icon} {c.name}</option>
                  ))}
                </select>
                <p className="text-xs text-white/30 mt-1">Si défini, le cumulé est calculé depuis les transactions de cette catégorie.</p>
              </div>
              <div className="flex gap-3 pt-2">
                <button type="submit" className="flex-1 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold">
                  Créer
                </button>
                <button type="button" onClick={() => setShowForm(false)} className="px-6 py-2.5 rounded-xl border border-white/10 text-white/60 hover:text-white">
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

export default DashboardProjects;
