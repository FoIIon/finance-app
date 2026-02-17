import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useDashboards } from '../hooks/useDashboards';
import { dashboardsApi } from '../api/dashboards';
import { accountsApi } from '../api/accounts';
import { invitationsApi } from '../api/invitations';
import type { DashboardDetail, Account, Invitation } from '../types/dashboard';

const DashboardSettings = () => {
  const { currentDashboard, refreshDashboards, setCurrentDashboard, dashboards } = useDashboards();
  const navigate = useNavigate();
  const [detail, setDetail] = useState<DashboardDetail | null>(null);
  const [userAccounts, setUserAccounts] = useState<Account[]>([]);
  const [invitations, setInvitations] = useState<Invitation[]>([]);
  const [loading, setLoading] = useState(true);

  // Renommer
  const [editName, setEditName] = useState('');
  const [isEditingName, setIsEditingName] = useState(false);

  // Inviter
  const [inviteEmail, setInviteEmail] = useState('');
  const [inviting, setInviting] = useState(false);
  const [inviteMessage, setInviteMessage] = useState('');

  // Ajouter un compte
  const [newAccountName, setNewAccountName] = useState('');
  const [creatingAccount, setCreatingAccount] = useState(false);

  // Supprimer
  const [deleteConfirm, setDeleteConfirm] = useState(false);

  useEffect(() => {
    if (!currentDashboard) return;
    const load = async () => {
      setLoading(true);
      try {
        const [detailRes, accountsRes, invitationsRes] = await Promise.all([
          dashboardsApi.getById(currentDashboard.id),
          accountsApi.getAll(),
          invitationsApi.getPending(currentDashboard.id),
        ]);
        setDetail(detailRes.data);
        setUserAccounts(accountsRes.data);
        setInvitations(invitationsRes.data);
        setEditName(detailRes.data.name);
      } catch {
        // Silently fail
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [currentDashboard]);

  const handleRename = async () => {
    if (!currentDashboard || !editName.trim()) return;
    await dashboardsApi.update(currentDashboard.id, { name: editName.trim() });
    await refreshDashboards();
    setIsEditingName(false);
    // Mettre à jour le detail local
    setDetail(prev => prev ? { ...prev, name: editName.trim() } : null);
  };

  const handleInvite = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!currentDashboard || !inviteEmail.trim()) return;
    setInviting(true);
    setInviteMessage('');
    try {
      await invitationsApi.send(currentDashboard.id, inviteEmail.trim());
      setInviteMessage('Invitation envoyée !');
      setInviteEmail('');
      const res = await invitationsApi.getPending(currentDashboard.id);
      setInvitations(res.data);
    } catch {
      setInviteMessage('Erreur lors de l\'envoi de l\'invitation.');
    } finally {
      setInviting(false);
    }
  };

  const handleCancelInvitation = async (id: number) => {
    if (!currentDashboard) return;
    await invitationsApi.cancel(id);
    const res = await invitationsApi.getPending(currentDashboard.id);
    setInvitations(res.data);
  };

  const handleAddAccount = async (accountId: number) => {
    if (!currentDashboard) return;
    await dashboardsApi.addAccount(currentDashboard.id, accountId);
    const res = await dashboardsApi.getById(currentDashboard.id);
    setDetail(res.data);
  };

  const handleRemoveAccount = async (accountId: number) => {
    if (!currentDashboard) return;
    await dashboardsApi.removeAccount(currentDashboard.id, accountId);
    const res = await dashboardsApi.getById(currentDashboard.id);
    setDetail(res.data);
  };

  const handleCreateAccount = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newAccountName.trim()) return;
    setCreatingAccount(true);
    try {
      await accountsApi.create({ name: newAccountName.trim() });
      setNewAccountName('');
      const res = await accountsApi.getAll();
      setUserAccounts(res.data);
    } catch {
      // Silently fail
    } finally {
      setCreatingAccount(false);
    }
  };

  const handleDelete = async () => {
    if (!currentDashboard) return;
    await dashboardsApi.delete(currentDashboard.id);
    await refreshDashboards();
    const remaining = dashboards.filter(d => d.id !== currentDashboard.id);
    if (remaining.length > 0) {
      setCurrentDashboard(remaining[0]);
    }
    navigate('/');
  };

  if (!currentDashboard) {
    return <div className="text-white/40">Aucun dashboard sélectionné.</div>;
  }

  if (loading) {
    return <div className="text-white/40">Chargement...</div>;
  }

  // Comptes du user pas encore liés au dashboard
  const unlinkedAccounts = userAccounts.filter(
    a => !detail?.accounts.find(da => da.id === a.id)
  );

  return (
    <div className="space-y-8 animate-[fadeIn_0.5s_ease-out] max-w-3xl">
      <h2 className="text-3xl font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
        Paramètres — {detail?.name}
      </h2>

      {/* Renommer */}
      <section className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-6">
        <h3 className="text-lg font-semibold text-white mb-4">Nom du dashboard</h3>
        {isEditingName ? (
          <div className="flex gap-3">
            <input
              type="text"
              value={editName}
              onChange={(e) => setEditName(e.target.value)}
              className="flex-1 px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
            />
            <button onClick={handleRename} className="px-4 py-2.5 rounded-xl bg-amber-500/20 text-amber-400 font-medium hover:bg-amber-500/30 transition-colors">
              Sauvegarder
            </button>
            <button onClick={() => setIsEditingName(false)} className="px-4 py-2.5 rounded-xl border border-white/10 text-white/60 hover:text-white hover:bg-white/5 transition-colors">
              Annuler
            </button>
          </div>
        ) : (
          <div className="flex items-center gap-3">
            <span className="text-white/70">{detail?.name}</span>
            <button onClick={() => setIsEditingName(true)} className="text-amber-400 hover:text-amber-300 text-sm">
              Renommer
            </button>
          </div>
        )}
      </section>

      {/* Comptes liés */}
      <section className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-6">
        <h3 className="text-lg font-semibold text-white mb-4">Comptes liés</h3>
        <div className="space-y-2 mb-4">
          {detail?.accounts.map((a) => (
            <div key={a.id} className="flex items-center justify-between py-2 px-3 rounded-xl bg-white/5">
              <span className="text-white/70">{a.name}</span>
              <button
                onClick={() => handleRemoveAccount(a.id)}
                className="text-white/30 hover:text-red-400 text-sm transition-colors"
              >
                Retirer
              </button>
            </div>
          ))}
          {detail?.accounts.length === 0 && (
            <p className="text-white/30 text-sm">Aucun compte lié.</p>
          )}
        </div>

        {unlinkedAccounts.length > 0 && (
          <div className="border-t border-white/10 pt-4">
            <p className="text-white/40 text-sm mb-2">Ajouter un compte :</p>
            <div className="flex flex-wrap gap-2">
              {unlinkedAccounts.map((a) => (
                <button
                  key={a.id}
                  onClick={() => handleAddAccount(a.id)}
                  className="px-3 py-1.5 rounded-lg bg-white/5 border border-white/10 text-white/60 text-sm hover:bg-amber-500/10 hover:text-amber-400 hover:border-amber-500/30 transition-all"
                >
                  + {a.name}
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Créer un nouveau compte */}
        <div className="border-t border-white/10 pt-4 mt-4">
          <form onSubmit={handleCreateAccount} className="flex gap-2">
            <input
              type="text"
              value={newAccountName}
              onChange={(e) => setNewAccountName(e.target.value)}
              placeholder="Nouveau compte..."
              className="flex-1 px-3 py-2 rounded-lg bg-white/5 border border-white/10 text-white text-sm placeholder-white/30 focus:outline-none focus:border-amber-500/50"
            />
            <button
              type="submit"
              disabled={creatingAccount}
              className="px-4 py-2 rounded-lg bg-amber-500/20 text-amber-400 text-sm font-medium hover:bg-amber-500/30 transition-colors disabled:opacity-50"
            >
              Créer
            </button>
          </form>
        </div>
      </section>

      {/* Membres */}
      <section className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-6">
        <h3 className="text-lg font-semibold text-white mb-4">Membres</h3>
        <div className="space-y-2 mb-4">
          {detail?.members.map((m) => (
            <div key={m.userId} className="flex items-center justify-between py-2 px-3 rounded-xl bg-white/5">
              <span className="text-white/70">{m.email}</span>
              <span className="text-white/30 text-sm">
                Membre depuis {new Date(m.joinedAt).toLocaleDateString('fr-FR')}
              </span>
            </div>
          ))}
        </div>

        {/* Inviter */}
        <div className="border-t border-white/10 pt-4">
          <p className="text-white/40 text-sm mb-2">Inviter un utilisateur :</p>
          {inviteMessage && (
            <div className="mb-3 p-3 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-300 text-sm">
              {inviteMessage}
            </div>
          )}
          <form onSubmit={handleInvite} className="flex gap-2">
            <input
              type="email"
              value={inviteEmail}
              onChange={(e) => setInviteEmail(e.target.value)}
              placeholder="email@exemple.com"
              required
              className="flex-1 px-3 py-2 rounded-lg bg-white/5 border border-white/10 text-white text-sm placeholder-white/30 focus:outline-none focus:border-amber-500/50"
            />
            <button
              type="submit"
              disabled={inviting}
              className="px-4 py-2 rounded-lg bg-amber-500/20 text-amber-400 text-sm font-medium hover:bg-amber-500/30 transition-colors disabled:opacity-50"
            >
              Inviter
            </button>
          </form>
        </div>

        {/* Invitations en attente */}
        {invitations.length > 0 && (
          <div className="border-t border-white/10 pt-4 mt-4">
            <p className="text-white/40 text-sm mb-2">Invitations en attente :</p>
            <div className="space-y-2">
              {invitations.map((inv) => (
                <div key={inv.id} className="flex items-center justify-between py-2 px-3 rounded-xl bg-white/5">
                  <div>
                    <span className="text-white/70 text-sm">{inv.invitedEmail}</span>
                    <span className="text-white/30 text-xs ml-2">
                      Expire le {new Date(inv.expiresAt).toLocaleDateString('fr-FR')}
                    </span>
                  </div>
                  <button
                    onClick={() => handleCancelInvitation(inv.id)}
                    className="text-white/30 hover:text-red-400 text-sm transition-colors"
                  >
                    Annuler
                  </button>
                </div>
              ))}
            </div>
          </div>
        )}
      </section>

      {/* Supprimer */}
      {detail?.isCreator && (
        <section className="bg-red-500/5 backdrop-blur-xl rounded-2xl border border-red-500/20 p-6">
          <h3 className="text-lg font-semibold text-red-400 mb-2">Zone dangereuse</h3>
          <p className="text-white/40 text-sm mb-4">
            La suppression du dashboard est irréversible.
          </p>
          {deleteConfirm ? (
            <div className="flex gap-3">
              <button
                onClick={handleDelete}
                className="px-4 py-2 rounded-xl bg-red-500 text-white font-medium hover:bg-red-600 transition-colors"
              >
                Confirmer la suppression
              </button>
              <button
                onClick={() => setDeleteConfirm(false)}
                className="px-4 py-2 rounded-xl border border-white/10 text-white/60 hover:text-white hover:bg-white/5 transition-colors"
              >
                Annuler
              </button>
            </div>
          ) : (
            <button
              onClick={() => setDeleteConfirm(true)}
              className="px-4 py-2 rounded-xl border border-red-500/30 text-red-400 hover:bg-red-500/10 transition-colors"
            >
              Supprimer ce dashboard
            </button>
          )}
        </section>
      )}
    </div>
  );
};

export default DashboardSettings;
