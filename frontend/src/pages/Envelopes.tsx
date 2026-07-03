import { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useDashboards } from '../hooks/useDashboards';
import { useProjectEnvelopesQuery, useShoppingItemsQuery } from '../hooks/queries';
import { projectEnvelopesApi } from '../api/projectEnvelopes';
import { shoppingItemsApi } from '../api/shoppingItems';
import type { ProjectEnvelopeProgress, CreateProjectEnvelope } from '../types/projectEnvelope';
import { TransactionType } from '../types/transaction';
import { formatCurrency } from '../utils/format';
import { useToast } from '../context/ToastContext';

interface EnvelopeForm {
  name: string;
  icon: string;
  targetBudget: string;
  fundingNote: string;
}

const emptyForm: EnvelopeForm = { name: '', icon: '🏖️', targetBudget: '', fundingNote: '' };

// ─── Modal transactions rattachées ─────────────────────────────────────────
const EnvelopeTransactionsModal = ({ envelope, onClose }: { envelope: ProjectEnvelopeProgress; onClose: () => void }) => {
  const { data: transactions, isLoading } = useQuery({
    queryKey: ['envelope-transactions', envelope.id],
    queryFn: async () => (await projectEnvelopesApi.getTransactions(envelope.id)).data,
  });

  useEffect(() => {
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onEsc);
    return () => document.removeEventListener('keydown', onEsc);
  }, [onClose]);

  const modal = (
    <div
      role="dialog"
      aria-modal="true"
      className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-end md:items-center justify-center"
      onClick={onClose}
    >
      <div
        className="bg-[#1a1a3e] rounded-t-2xl md:rounded-2xl border border-white/10 p-6 md:p-8 w-full md:max-w-2xl max-h-[85vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between mb-4">
          <div>
            <h3 className="text-xl font-bold text-white flex items-center gap-2">
              <span aria-hidden="true">{envelope.icon}</span> {envelope.name}
            </h3>
            <p className="text-white/50 text-sm mt-1">
              Engagé <span className="text-amber-400 font-semibold">{formatCurrency(envelope.spent)}</span>
              {envelope.targetBudget != null && (
                <> · sur {formatCurrency(envelope.targetBudget)}</>
              )}
            </p>
          </div>
          <button onClick={onClose} aria-label="Fermer" className="text-white/40 hover:text-white text-2xl leading-none px-2">×</button>
        </div>

        <div className="flex-1 overflow-y-auto -mx-2 px-2">
          {isLoading ? (
            <div className="space-y-2">
              {[1, 2, 3].map(i => <div key={i} className="h-12 rounded bg-white/5 animate-pulse" />)}
            </div>
          ) : !transactions || transactions.length === 0 ? (
            <p className="text-white/30 text-center py-8 text-sm">Aucune dépense rattachée pour l'instant</p>
          ) : (
            <ul className="divide-y divide-white/5">
              {transactions.map((t) => (
                <li key={t.id} className="py-3 flex items-start justify-between gap-3">
                  <div className="min-w-0 flex-1">
                    <p className="text-white text-sm truncate">
                      {t.isExceptional && <span className="text-amber-400 mr-1" title="Dépense exceptionnelle">⚡</span>}
                      {t.description || <em className="text-white/30">(sans libellé)</em>}
                    </p>
                    <p className="text-white/40 text-xs mt-0.5">
                      {new Date(t.date).toLocaleDateString('fr-FR')} · {t.categoryIcon} {t.categoryName}
                    </p>
                  </div>
                  <span className={`text-sm font-semibold flex-shrink-0 ${t.type === TransactionType.Income ? 'text-emerald-400' : 'text-red-400'}`}>
                    {t.type === TransactionType.Income ? '+' : '-'}{formatCurrency(t.amount)}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>

        {transactions && transactions.length > 0 && (
          <div className="mt-4 pt-3 border-t border-white/10 text-white/40 text-xs text-right">
            {transactions.length} transaction{transactions.length > 1 ? 's' : ''}
          </div>
        )}
      </div>
    </div>
  );

  return createPortal(modal, document.body);
};

// ─── Carte enveloppe ───────────────────────────────────────────────────────
const EnvelopeCard = ({
  e,
  onOpen,
  onEdit,
  onArchive,
  onDelete,
  deleteConfirm,
  onDeleteConfirm,
}: {
  e: ProjectEnvelopeProgress;
  onOpen: () => void;
  onEdit: () => void;
  onArchive: () => void;
  onDelete: () => void;
  deleteConfirm: boolean;
  onDeleteConfirm: (v: boolean) => void;
}) => {
  const pct = e.targetBudget && e.targetBudget > 0
    ? Math.min(100, Math.round((e.spent / e.targetBudget) * 100))
    : null;
  const over = e.remaining != null && e.remaining < 0;

  return (
    <div className={`bg-white/5 rounded-2xl border border-white/10 p-5 flex flex-col ${e.isArchived ? 'opacity-60' : ''}`}>
      <div className="flex items-start justify-between gap-3">
        <button onClick={onOpen} className="flex items-start gap-3 text-left min-w-0 flex-1 group">
          <span className="text-2xl flex-shrink-0" aria-hidden="true">{e.icon}</span>
          <div className="min-w-0">
            <h3 className="text-white font-semibold truncate group-hover:text-amber-400 transition-colors">{e.name}</h3>
            {e.fundingNote && <p className="text-white/40 text-xs mt-0.5 truncate">{e.fundingNote}</p>}
          </div>
        </button>
        <div className="flex items-center gap-1.5 flex-shrink-0">
          <button onClick={onEdit} aria-label="Modifier" className="text-white/40 hover:text-amber-400 transition-colors text-sm">✏️</button>
          <button onClick={onArchive} aria-label={e.isArchived ? 'Désarchiver' : 'Archiver'} title={e.isArchived ? 'Désarchiver' : 'Archiver'} className="text-white/40 hover:text-amber-400 transition-colors text-sm">
            {e.isArchived ? '📤' : '📥'}
          </button>
          {deleteConfirm ? (
            <>
              <button onClick={onDelete} className="text-red-400 hover:text-red-300 text-xs font-medium">Confirmer</button>
              <button onClick={() => onDeleteConfirm(false)} className="text-white/40 hover:text-white text-xs">Annuler</button>
            </>
          ) : (
            <button onClick={() => onDeleteConfirm(true)} aria-label="Supprimer" className="text-white/40 hover:text-red-400 transition-colors text-sm">🗑️</button>
          )}
        </div>
      </div>

      {pct != null && (
        <div className="mt-4">
          <div className="h-2 rounded-full bg-white/10 overflow-hidden">
            <div
              className={`h-full rounded-full transition-all ${over ? 'bg-red-500' : 'bg-gradient-to-r from-amber-500 to-orange-500'}`}
              style={{ width: `${pct}%` }}
            />
          </div>
        </div>
      )}

      <div className="mt-4 grid grid-cols-3 gap-2 text-center">
        <div>
          <p className="text-white/40 text-xs">Engagé</p>
          <p className="text-white font-semibold text-sm mt-0.5">{formatCurrency(e.spent)}</p>
        </div>
        <div>
          <p className="text-white/40 text-xs">Cible</p>
          <p className="text-white/70 font-semibold text-sm mt-0.5">{e.targetBudget != null ? formatCurrency(e.targetBudget) : '—'}</p>
        </div>
        <div>
          <p className="text-white/40 text-xs">Restant</p>
          <p className={`font-semibold text-sm mt-0.5 ${e.remaining == null ? 'text-white/70' : over ? 'text-red-400' : 'text-emerald-400'}`}>
            {e.remaining != null ? formatCurrency(e.remaining) : '—'}
          </p>
        </div>
      </div>

      <button onClick={onOpen} className="mt-4 text-white/40 hover:text-white/70 text-xs text-left transition-colors">
        {e.transactionCount} dépense{e.transactionCount > 1 ? 's' : ''} rattachée{e.transactionCount > 1 ? 's' : ''} →
      </button>
    </div>
  );
};

// ─── Section « À acheter » ─────────────────────────────────────────────────
const ShoppingList = ({ dashboardId }: { dashboardId: number }) => {
  const { data: items, isLoading } = useShoppingItemsQuery(dashboardId);
  const qc = useQueryClient();
  const { showToast } = useToast();
  const [label, setLabel] = useState('');
  const [cost, setCost] = useState('');

  const invalidate = () => qc.invalidateQueries({ queryKey: ['shopping-items', dashboardId] });

  const handleAdd = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!label.trim()) return;
    try {
      await shoppingItemsApi.create({
        dashboardId,
        label: label.trim(),
        estimatedCost: cost ? parseFloat(cost) : null,
      });
      setLabel('');
      setCost('');
      invalidate();
    } catch {
      showToast('Impossible d\'ajouter l\'article', 'error');
    }
  };

  const handleToggle = async (id: number) => {
    try { await shoppingItemsApi.toggle(id); invalidate(); }
    catch { showToast('Impossible de mettre à jour l\'article', 'error'); }
  };

  const handleDelete = async (id: number) => {
    try { await shoppingItemsApi.delete(id); invalidate(); }
    catch { showToast('Impossible de supprimer l\'article', 'error'); }
  };

  const pendingTotal = (items ?? [])
    .filter(i => !i.isDone && i.estimatedCost != null)
    .reduce((s, i) => s + (i.estimatedCost ?? 0), 0);

  return (
    <div className="bg-white/5 rounded-2xl border border-white/10 p-5">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-white font-semibold">À acheter</h3>
        {pendingTotal > 0 && (
          <span className="text-white/50 text-sm">≈ <span className="text-amber-400 font-semibold">{formatCurrency(pendingTotal)}</span></span>
        )}
      </div>

      <form onSubmit={handleAdd} className="flex gap-2 mb-4">
        <input
          type="text"
          value={label}
          onChange={(e) => setLabel(e.target.value)}
          placeholder="Article..."
          className="flex-1 min-w-0 px-3 py-2 rounded-xl bg-white/5 border border-white/10 text-white placeholder-white/30 text-sm focus:outline-none focus:border-amber-500/50"
        />
        <input
          type="number"
          step="0.01"
          min="0"
          value={cost}
          onChange={(e) => setCost(e.target.value)}
          placeholder="€"
          className="w-20 px-3 py-2 rounded-xl bg-white/5 border border-white/10 text-white placeholder-white/30 text-sm focus:outline-none focus:border-amber-500/50"
        />
        <button type="submit" className="px-3 py-2 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white text-sm font-semibold hover:from-amber-600 hover:to-orange-700 transition-all">+</button>
      </form>

      {isLoading ? (
        <div className="space-y-2">{[1, 2, 3].map(i => <div key={i} className="h-9 rounded bg-white/5 animate-pulse" />)}</div>
      ) : !items || items.length === 0 ? (
        <p className="text-white/30 text-center py-6 text-sm">Rien à acheter pour l'instant</p>
      ) : (
        <ul className="space-y-1">
          {items.map((i) => (
            <li key={i.id} className="flex items-center gap-3 py-2 group">
              <input
                type="checkbox"
                checked={i.isDone}
                onChange={() => handleToggle(i.id)}
                className="w-4 h-4 flex-shrink-0 accent-amber-500 cursor-pointer"
                aria-label={i.isDone ? 'Marquer à faire' : 'Marquer fait'}
              />
              <span className={`flex-1 min-w-0 truncate text-sm ${i.isDone ? 'line-through text-white/30' : 'text-white/80'}`}>
                {i.label}
              </span>
              {i.estimatedCost != null && (
                <span className={`text-xs flex-shrink-0 ${i.isDone ? 'text-white/20' : 'text-white/50'}`}>{formatCurrency(i.estimatedCost)}</span>
              )}
              <button
                onClick={() => handleDelete(i.id)}
                aria-label="Supprimer"
                className="text-white/20 hover:text-red-400 transition-colors opacity-0 group-hover:opacity-100 flex-shrink-0"
              >×</button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};

// ─── Page ──────────────────────────────────────────────────────────────────
const Envelopes = () => {
  const { currentDashboard } = useDashboards();
  const dashboardId = currentDashboard?.id;
  const { data: envelopes, isLoading } = useProjectEnvelopesQuery(dashboardId);
  const qc = useQueryClient();
  const { showToast } = useToast();

  const [showForm, setShowForm] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<EnvelopeForm>(emptyForm);
  const [deleteConfirm, setDeleteConfirm] = useState<number | null>(null);
  const [openEnvelope, setOpenEnvelope] = useState<ProjectEnvelopeProgress | null>(null);

  const invalidate = () => qc.invalidateQueries({ queryKey: ['project-envelopes', dashboardId] });

  const active = (envelopes ?? []).filter(e => !e.isArchived);
  const archived = (envelopes ?? []).filter(e => e.isArchived);

  const startCreate = () => { setEditingId(null); setForm(emptyForm); setShowForm(true); };
  const startEdit = (e: ProjectEnvelopeProgress) => {
    setEditingId(e.id);
    setForm({
      name: e.name,
      icon: e.icon,
      targetBudget: e.targetBudget != null ? String(e.targetBudget) : '',
      fundingNote: e.fundingNote ?? '',
    });
    setShowForm(true);
  };

  const handleSubmit = async (ev: React.FormEvent) => {
    ev.preventDefault();
    if (!dashboardId) return;
    const targetBudget = form.targetBudget ? parseFloat(form.targetBudget) : null;
    try {
      if (editingId) {
        await projectEnvelopesApi.update(editingId, {
          name: form.name,
          icon: form.icon,
          targetBudget,
          fundingNote: form.fundingNote,
        });
        showToast('Enveloppe mise à jour', 'success');
      } else {
        const payload: CreateProjectEnvelope = {
          dashboardId,
          name: form.name,
          icon: form.icon,
          targetBudget,
          fundingNote: form.fundingNote || null,
        };
        await projectEnvelopesApi.create(payload);
        showToast('Enveloppe créée', 'success');
      }
      setShowForm(false);
      setEditingId(null);
      setForm(emptyForm);
      invalidate();
    } catch {
      showToast('Erreur lors de la sauvegarde', 'error');
    }
  };

  const handleArchive = async (e: ProjectEnvelopeProgress) => {
    try {
      await projectEnvelopesApi.update(e.id, { isArchived: !e.isArchived });
      invalidate();
    } catch {
      showToast('Impossible d\'archiver l\'enveloppe', 'error');
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await projectEnvelopesApi.delete(id);
      setDeleteConfirm(null);
      invalidate();
      showToast('Enveloppe supprimée', 'success');
    } catch {
      showToast('Erreur lors de la suppression', 'error');
    }
  };

  return (
    <div className="space-y-6 animate-[fadeIn_0.15s_ease-out]">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl md:text-3xl font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
            Enveloppes {currentDashboard ? `— ${currentDashboard.name}` : ''}
          </h2>
          <p className="text-white/40 text-sm mt-1">
            Budgète tes gros projets (vacances, ski) et rattache les dépenses
          </p>
        </div>
        <button
          onClick={startCreate}
          className="px-4 py-2 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white text-sm font-semibold hover:from-amber-600 hover:to-orange-700 transition-all whitespace-nowrap"
        >
          + Nouvelle enveloppe
        </button>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Enveloppes */}
        <div className="lg:col-span-2 space-y-4">
          {isLoading ? (
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              {[1, 2].map(i => <div key={i} className="h-48 rounded-2xl bg-white/5 animate-pulse" />)}
            </div>
          ) : active.length === 0 && archived.length === 0 ? (
            <div className="bg-white/5 rounded-2xl border border-white/10 p-8 text-center">
              <p className="text-white/40 text-sm">Aucune enveloppe projet.</p>
              <p className="text-white/30 text-xs mt-1">Crée-en une pour suivre le budget d'un gros projet.</p>
            </div>
          ) : (
            <>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {active.map((e) => (
                  <EnvelopeCard
                    key={e.id}
                    e={e}
                    onOpen={() => setOpenEnvelope(e)}
                    onEdit={() => startEdit(e)}
                    onArchive={() => handleArchive(e)}
                    onDelete={() => handleDelete(e.id)}
                    deleteConfirm={deleteConfirm === e.id}
                    onDeleteConfirm={(v) => setDeleteConfirm(v ? e.id : null)}
                  />
                ))}
              </div>

              {archived.length > 0 && (
                <details className="group">
                  <summary className="cursor-pointer text-white/40 hover:text-white/60 text-sm py-2 select-none">
                    Archivées ({archived.length})
                  </summary>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mt-3">
                    {archived.map((e) => (
                      <EnvelopeCard
                        key={e.id}
                        e={e}
                        onOpen={() => setOpenEnvelope(e)}
                        onEdit={() => startEdit(e)}
                        onArchive={() => handleArchive(e)}
                        onDelete={() => handleDelete(e.id)}
                        deleteConfirm={deleteConfirm === e.id}
                        onDeleteConfirm={(v) => setDeleteConfirm(v ? e.id : null)}
                      />
                    ))}
                  </div>
                </details>
              )}
            </>
          )}
        </div>

        {/* À acheter */}
        <div className="lg:col-span-1">
          {dashboardId && <ShoppingList dashboardId={dashboardId} />}
        </div>
      </div>

      {/* Modal formulaire */}
      {showForm && (
        <div role="dialog" aria-modal="true" className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-end md:items-center justify-center p-0 md:p-4" onClick={() => setShowForm(false)}>
          <div className="bg-[#1a1a3e] rounded-t-2xl md:rounded-2xl border border-white/10 p-6 w-full md:max-w-md max-h-[90vh] overflow-y-auto" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-xl font-bold text-white mb-4">{editingId ? 'Modifier l\'enveloppe' : 'Nouvelle enveloppe'}</h3>
            <form onSubmit={handleSubmit} className="space-y-4">
              <div className="flex gap-3">
                <div className="w-20">
                  <label className="block text-sm text-white/60 mb-1">Icône</label>
                  <input
                    type="text"
                    value={form.icon}
                    onChange={(e) => setForm({ ...form, icon: e.target.value })}
                    maxLength={2}
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white text-center focus:outline-none focus:border-amber-500/50"
                  />
                </div>
                <div className="flex-1">
                  <label className="block text-sm text-white/60 mb-1">Nom</label>
                  <input
                    type="text"
                    required
                    value={form.name}
                    onChange={(e) => setForm({ ...form, name: e.target.value })}
                    placeholder="Vacances camping"
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white placeholder-white/30 focus:outline-none focus:border-amber-500/50"
                  />
                </div>
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Budget cible (€) <span className="text-white/30">— optionnel</span></label>
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={form.targetBudget}
                  onChange={(e) => setForm({ ...form, targetBudget: e.target.value })}
                  placeholder="1626"
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white placeholder-white/30 focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div>
                <label className="block text-sm text-white/60 mb-1">Note de financement <span className="text-white/30">— optionnel</span></label>
                <input
                  type="text"
                  value={form.fundingNote}
                  onChange={(e) => setForm({ ...form, fundingNote: e.target.value })}
                  placeholder="prime fin d'année"
                  className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white placeholder-white/30 focus:outline-none focus:border-amber-500/50"
                />
              </div>
              <div className="flex gap-3 pt-2">
                <button type="submit" className="flex-1 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all">
                  {editingId ? 'Enregistrer' : 'Créer'}
                </button>
                <button type="button" onClick={() => { setShowForm(false); setEditingId(null); }} className="px-6 py-2.5 rounded-xl border border-white/10 text-white/60 hover:text-white hover:bg-white/5 transition-all">
                  Annuler
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {openEnvelope && <EnvelopeTransactionsModal envelope={openEnvelope} onClose={() => setOpenEnvelope(null)} />}
    </div>
  );
};

export default Envelopes;
