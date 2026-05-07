import { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { manualAccountsApi } from '../api/manualAccounts';
import { useAccountBalancesQuery } from '../hooks/queries';
import { useDashboards } from '../hooks/useDashboards';
import { categoriesApi } from '../api/categories';
import type { Category } from '../types/category';
import type { ManualAccount } from '../types/savingsGoal';
import { formatCurrency } from '../utils/format';
import { useToast } from '../context/ToastContext';

const ManualAccountsSection = () => {
  const { currentDashboard } = useDashboards();
  const qc = useQueryClient();
  const { showToast } = useToast();

  const { data: manualAccounts, isLoading } = useQuery({
    queryKey: ['manual-accounts'],
    queryFn: async () => (await manualAccountsApi.getAll()).data,
  });

  const { data: balances } = useAccountBalancesQuery(currentDashboard?.id);
  const [categories, setCategories] = useState<Category[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<ManualAccount | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<number | null>(null);
  const [form, setForm] = useState({
    name: '',
    iban: '',
    initialBalance: 0,
    initialBalanceDate: new Date().toISOString().split('T')[0],
    sourceBankAccountId: 0,
    incrementCategoryId: 16, // Épargne par défaut
  });

  useEffect(() => {
    categoriesApi.getAll().then(r => setCategories(r.data));
  }, []);

  const connectedAccounts = balances?.filter(b => !b.isManual) ?? [];

  const openCreate = () => {
    setEditing(null);
    setForm({
      name: '',
      iban: '',
      initialBalance: 0,
      initialBalanceDate: new Date().toISOString().split('T')[0],
      sourceBankAccountId: connectedAccounts[0]?.accountId ?? 0,
      incrementCategoryId: 16,
    });
    setShowForm(true);
  };

  const openEdit = (a: ManualAccount) => {
    setEditing(a);
    setForm({
      name: a.name,
      iban: a.iban,
      initialBalance: a.initialBalance,
      initialBalanceDate: a.initialBalanceDate.split('T')[0],
      sourceBankAccountId: a.sourceBankAccountId ?? 0,
      incrementCategoryId: a.incrementCategoryId ?? 16,
    });
    setShowForm(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (editing) {
        await manualAccountsApi.update(editing.id, {
          name: form.name,
          iban: form.iban,
          initialBalance: form.initialBalance,
          initialBalanceDate: new Date(form.initialBalanceDate).toISOString(),
          sourceBankAccountId: form.sourceBankAccountId || undefined,
          incrementCategoryId: form.incrementCategoryId || undefined,
        });
        showToast('Compte mis à jour', 'success');
      } else {
        await manualAccountsApi.create({
          name: form.name,
          iban: form.iban || undefined,
          initialBalance: form.initialBalance,
          initialBalanceDate: new Date(form.initialBalanceDate).toISOString(),
          sourceBankAccountId: form.sourceBankAccountId || undefined,
          incrementCategoryId: form.incrementCategoryId || undefined,
        });
        showToast('Compte créé', 'success');
      }
      qc.invalidateQueries({ queryKey: ['manual-accounts'] });
      qc.invalidateQueries({ queryKey: ['account-balances'] });
      setShowForm(false);
    } catch {
      showToast('Erreur', 'error');
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await manualAccountsApi.delete(id);
      qc.invalidateQueries({ queryKey: ['manual-accounts'] });
      qc.invalidateQueries({ queryKey: ['account-balances'] });
      setDeleteConfirm(null);
      showToast('Compte supprimé', 'success');
    } catch {
      showToast('Erreur', 'error');
    }
  };

  return (
    <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5 md:p-6">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h3 className="text-lg font-semibold text-white">Comptes manuels</h3>
          <p className="text-white/40 text-xs mt-0.5">
            Pour les comptes non-connectables (épargne, livret…). Solde calculé automatiquement à partir des transferts.
          </p>
        </div>
        <button
          onClick={openCreate}
          className="px-3 py-2 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white text-sm font-semibold hover:from-amber-600 hover:to-orange-700 transition-all"
        >
          + Ajouter
        </button>
      </div>

      {isLoading ? (
        <div className="h-12 rounded bg-white/5 animate-pulse" />
      ) : !manualAccounts || manualAccounts.length === 0 ? (
        <p className="text-white/30 text-center text-sm py-4">Aucun compte manuel défini.</p>
      ) : (
        <div className="space-y-2">
          {manualAccounts.map((a) => (
            <div key={a.id} className="flex items-center justify-between gap-3 py-3 border-b border-white/5 last:border-0">
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <span aria-hidden="true">💰</span>
                  <p className="text-white font-medium truncate">{a.name}</p>
                </div>
                <p className="text-white/40 text-xs truncate">
                  {a.iban && <>{a.iban} · </>}
                  Initial : {formatCurrency(a.initialBalance)} le {new Date(a.initialBalanceDate).toLocaleDateString('fr-FR')}
                  {a.sourceBankAccountName && <> · alimenté par {a.sourceBankAccountName}</>}
                  {a.incrementCategoryName && <> via {a.incrementCategoryName}</>}
                </p>
              </div>
              <div className="flex items-center gap-2">
                <button onClick={() => openEdit(a)} className="text-white/40 hover:text-amber-400" aria-label="Modifier">✏️</button>
                {deleteConfirm === a.id ? (
                  <>
                    <button onClick={() => handleDelete(a.id)} className="text-red-400 text-sm">Confirmer</button>
                    <button onClick={() => setDeleteConfirm(null)} className="text-white/40 text-sm">Annuler</button>
                  </>
                ) : (
                  <button onClick={() => setDeleteConfirm(a.id)} className="text-white/40 hover:text-red-400" aria-label="Supprimer">🗑️</button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {showForm && createPortal(
        <div role="dialog" aria-modal="true" className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4" onClick={() => setShowForm(false)}>
          <div className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-6 w-full max-w-md max-h-[90vh] overflow-y-auto" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-white mb-4">
              {editing ? 'Modifier le compte' : 'Nouveau compte manuel'}
            </h3>
            <form onSubmit={handleSubmit} className="space-y-4">
              <div>
                <label className="block text-sm text-white/60 mb-1">Nom du compte</label>
                <input
                  type="text"
                  required
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  placeholder="Ex: Argenta Épargne commune"
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">IBAN (optionnel)</label>
                <input
                  type="text"
                  value={form.iban}
                  onChange={(e) => setForm({ ...form, iban: e.target.value })}
                  placeholder="BE..."
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-sm text-white/60 mb-1">Solde actuel (€)</label>
                  <input
                    type="number"
                    required
                    step="0.01"
                    value={form.initialBalance || ''}
                    onChange={(e) => setForm({ ...form, initialBalance: parseFloat(e.target.value) || 0 })}
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                  />
                </div>
                <div>
                  <label className="block text-sm text-white/60 mb-1">Date du solde</label>
                  <input
                    type="date"
                    required
                    value={form.initialBalanceDate}
                    onChange={(e) => setForm({ ...form, initialBalanceDate: e.target.value })}
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                  />
                </div>
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Compte source (alimente)</label>
                <select
                  value={form.sourceBankAccountId}
                  onChange={(e) => setForm({ ...form, sourceBankAccountId: Number(e.target.value) })}
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                >
                  <option value={0}>— Aucun (solde fixe) —</option>
                  {connectedAccounts.map(a => (
                    <option key={a.accountId} value={a.accountId}>
                      {a.bankInstitutionName ?? 'Compte'} — {a.accountName}
                    </option>
                  ))}
                </select>
                <p className="text-xs text-white/30 mt-1">Compte courant d'où partent les transferts vers ce compte.</p>
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Catégorie de transfert</label>
                <select
                  value={form.incrementCategoryId}
                  onChange={(e) => setForm({ ...form, incrementCategoryId: Number(e.target.value) })}
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                >
                  <option value={0}>— Aucune (solde fixe) —</option>
                  {categories.map(c => (
                    <option key={c.id} value={c.id}>{c.icon} {c.name}</option>
                  ))}
                </select>
                <p className="text-xs text-white/30 mt-1">Catégorie qui marque les transferts à compter (ex: Épargne).</p>
              </div>
              <div className="flex gap-3 pt-2">
                <button type="submit" className="flex-1 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold">
                  {editing ? 'Enregistrer' : 'Créer'}
                </button>
                <button type="button" onClick={() => setShowForm(false)} className="px-6 py-2.5 rounded-xl border border-white/10 text-white/60 hover:text-white">
                  Annuler
                </button>
              </div>
            </form>
          </div>
        </div>,
        document.body
      )}
    </div>
  );
};

export default ManualAccountsSection;
