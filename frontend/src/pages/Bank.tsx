import { useState, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useBanking } from '../hooks/useBanking';
import { categoriesApi } from '../api/categories';
import { BankConnectionStatus } from '../types/banking';
import type { BankConnection, BankAccount, CategoryRule, CreateCategoryRule } from '../types/banking';
import type { Category } from '../types/category';

const EU_COUNTRIES = [
  { code: 'FR', label: 'France' },
  { code: 'DE', label: 'Allemagne' },
  { code: 'ES', label: 'Espagne' },
  { code: 'IT', label: 'Italie' },
  { code: 'BE', label: 'Belgique' },
  { code: 'NL', label: 'Pays-Bas' },
  { code: 'PT', label: 'Portugal' },
  { code: 'AT', label: 'Autriche' },
  { code: 'IE', label: 'Irlande' },
  { code: 'FI', label: 'Finlande' },
];

const statusLabel = (status: BankConnectionStatus) => {
  switch (status) {
    case BankConnectionStatus.Linked: return 'Connectée';
    case BankConnectionStatus.Expired: return 'Expirée';
    case BankConnectionStatus.Error: return 'Erreur';
  }
};

const statusColor = (status: BankConnectionStatus) => {
  switch (status) {
    case BankConnectionStatus.Linked: return 'bg-emerald-500/20 text-emerald-400 border-emerald-500/30';
    case BankConnectionStatus.Expired: return 'bg-orange-500/20 text-orange-400 border-orange-500/30';
    case BankConnectionStatus.Error: return 'bg-red-500/20 text-red-400 border-red-500/30';
  }
};

const Bank = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const {
    connections, institutions, categoryRules, loading, error,
    fetchInstitutions, connectBank, handleCallback,
    deleteConnection, syncConnection, updateAccount,
    fetchCategoryRules, createCategoryRule, updateCategoryRule, deleteCategoryRule,
  } = useBanking();

  const [categories, setCategories] = useState<Category[]>([]);
  const [showConnectModal, setShowConnectModal] = useState(false);
  const [selectedCountry, setSelectedCountry] = useState('FR');
  const [institutionSearch, setInstitutionSearch] = useState('');
  const [connectingInstitution, setConnectingInstitution] = useState<string | null>(null);
  const [expandedConnections, setExpandedConnections] = useState<Set<number>>(new Set());
  const [deleteConfirm, setDeleteConfirm] = useState<number | null>(null);
  const [syncingId, setSyncingId] = useState<number | null>(null);
  const [syncError, setSyncError] = useState<string | null>(null);
  const [callbackProcessing, setCallbackProcessing] = useState(false);

  // Règles de catégorisation
  const [ruleKeyword, setRuleKeyword] = useState('');
  const [ruleCategoryId, setRuleCategoryId] = useState<number>(0);
  const [editingRule, setEditingRule] = useState<CategoryRule | null>(null);
  const [editKeyword, setEditKeyword] = useState('');
  const [editCategoryId, setEditCategoryId] = useState<number>(0);
  const [ruleDeleteConfirm, setRuleDeleteConfirm] = useState<number | null>(null);

  // Gestion du callback GoCardless
  useEffect(() => {
    const ref = searchParams.get('ref');
    if (ref) {
      setCallbackProcessing(true);
      handleCallback(ref).finally(() => {
        setCallbackProcessing(false);
        setSearchParams({}, { replace: true });
      });
    }
  }, [searchParams, handleCallback, setSearchParams]);

  // Charger les catégories et règles
  useEffect(() => {
    categoriesApi.getAll().then((res) => setCategories(res.data));
    fetchCategoryRules();
  }, [fetchCategoryRules]);

  // Charger les institutions quand le modal s'ouvre ou que le pays change
  useEffect(() => {
    if (showConnectModal) {
      fetchInstitutions(selectedCountry);
    }
  }, [showConnectModal, selectedCountry, fetchInstitutions]);

  const handleConnect = async (institution: { id: string; name: string; logo: string }) => {
    setConnectingInstitution(institution.id);
    try {
      const url = await connectBank({
        institutionId: institution.id,
        institutionName: institution.name,
        institutionLogo: institution.logo,
      });
      window.location.href = url;
    } catch {
      setConnectingInstitution(null);
    }
  };

  const handleSync = async (id: number) => {
    setSyncingId(id);
    setSyncError(null);
    try {
      await syncConnection(id);
    } catch (err: unknown) {
      const message = err instanceof Error && 'response' in err
        ? (err as { response?: { data?: string } }).response?.data ?? 'Erreur lors de la synchronisation'
        : 'Erreur lors de la synchronisation';
      setSyncError(typeof message === 'string' ? message : 'Erreur lors de la synchronisation');
    } finally {
      setSyncingId(null);
    }
  };

  const handleDelete = async (id: number) => {
    await deleteConnection(id);
    setDeleteConfirm(null);
  };

  const toggleExpanded = (id: number) => {
    setExpandedConnections((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleToggleAccount = async (account: BankAccount) => {
    await updateAccount(account.id, !account.isActive);
  };

  const handleAddRule = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!ruleKeyword.trim() || ruleCategoryId === 0) return;
    const data: CreateCategoryRule = { keyword: ruleKeyword.trim(), categoryId: ruleCategoryId };
    await createCategoryRule(data);
    setRuleKeyword('');
    setRuleCategoryId(0);
  };

  const handleStartEditRule = (rule: CategoryRule) => {
    setEditingRule(rule);
    setEditKeyword(rule.keyword);
    setEditCategoryId(rule.categoryId);
  };

  const handleSaveEditRule = async () => {
    if (!editingRule) return;
    await updateCategoryRule(editingRule.id, { keyword: editKeyword.trim(), categoryId: editCategoryId });
    setEditingRule(null);
  };

  const handleDeleteRule = async (id: number) => {
    await deleteCategoryRule(id);
    setRuleDeleteConfirm(null);
  };

  const filteredInstitutions = institutions.filter((i) =>
    i.name.toLowerCase().includes(institutionSearch.toLowerCase())
  );

  if (callbackProcessing) {
    return (
      <div className="flex items-center justify-center h-64 animate-[fadeIn_0.5s_ease-out]">
        <div className="text-white/40">Connexion en cours...</div>
      </div>
    );
  }

  return (
    <div className="space-y-8 animate-[fadeIn_0.5s_ease-out]">
      {/* En-tête connexions bancaires */}
      <div className="flex items-center justify-between">
        <h2 className="text-3xl font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
          Comptes bancaires
        </h2>
        <button
          onClick={() => setShowConnectModal(true)}
          className="px-5 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200"
        >
          + Connecter une banque
        </button>
      </div>

      {(error || syncError) && (
        <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-sm">
          {error || syncError}
        </div>
      )}

      {/* Liste des connexions */}
      {loading ? (
        <div className="p-8 text-center text-white/40">Chargement...</div>
      ) : connections.length === 0 ? (
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-8 text-center text-white/30">
          Aucune banque connectée
        </div>
      ) : (
        <div className="space-y-4">
          {connections.map((conn) => (
            <ConnectionCard
              key={conn.id}
              connection={conn}
              expanded={expandedConnections.has(conn.id)}
              onToggleExpand={() => toggleExpanded(conn.id)}
              onSync={() => handleSync(conn.id)}
              syncing={syncingId === conn.id}
              deleteConfirm={deleteConfirm === conn.id}
              onDeleteRequest={() => setDeleteConfirm(conn.id)}
              onDeleteConfirm={() => handleDelete(conn.id)}
              onDeleteCancel={() => setDeleteConfirm(null)}
              onToggleAccount={handleToggleAccount}
            />
          ))}
        </div>
      )}

      {/* Section règles de catégorisation */}
      <div className="pt-4">
        <h2 className="text-3xl font-bold text-white mb-6" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
          Règles de catégorisation
        </h2>

        {/* Formulaire d'ajout */}
        <form onSubmit={handleAddRule} className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-4 mb-4 flex gap-4 items-end">
          <div className="flex-1">
            <label className="block text-sm text-white/60 mb-1">Mot-clé</label>
            <input
              type="text"
              value={ruleKeyword}
              onChange={(e) => setRuleKeyword(e.target.value)}
              placeholder="Ex: Carrefour, SNCF..."
              className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
            />
          </div>
          <div className="w-56">
            <label className="block text-sm text-white/60 mb-1">Catégorie</label>
            <select
              value={ruleCategoryId}
              onChange={(e) => setRuleCategoryId(Number(e.target.value))}
              className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
            >
              <option value={0} disabled>Sélectionner...</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>{c.icon} {c.name}</option>
              ))}
            </select>
          </div>
          <button
            type="submit"
            className="px-6 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all"
          >
            Ajouter
          </button>
        </form>

        {/* Tableau des règles */}
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 overflow-hidden">
          {categoryRules.length === 0 ? (
            <div className="p-8 text-center text-white/30">Aucune règle de catégorisation</div>
          ) : (
            <table className="w-full">
              <thead>
                <tr className="border-b border-white/10">
                  <th className="text-left p-4 text-white/40 font-medium text-sm">Mot-clé</th>
                  <th className="text-left p-4 text-white/40 font-medium text-sm">Catégorie</th>
                  <th className="text-right p-4 text-white/40 font-medium text-sm">Actions</th>
                </tr>
              </thead>
              <tbody>
                {categoryRules.map((rule) => (
                  <tr key={rule.id} className="border-b border-white/5 hover:bg-white/5 transition-colors">
                    {editingRule?.id === rule.id ? (
                      <>
                        <td className="p-4">
                          <input
                            type="text"
                            value={editKeyword}
                            onChange={(e) => setEditKeyword(e.target.value)}
                            className="w-full px-3 py-1.5 rounded-lg bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                          />
                        </td>
                        <td className="p-4">
                          <select
                            value={editCategoryId}
                            onChange={(e) => setEditCategoryId(Number(e.target.value))}
                            className="w-full px-3 py-1.5 rounded-lg bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                          >
                            {categories.map((c) => (
                              <option key={c.id} value={c.id}>{c.icon} {c.name}</option>
                            ))}
                          </select>
                        </td>
                        <td className="p-4 text-right space-x-2">
                          <button onClick={handleSaveEditRule} className="text-emerald-400 hover:text-emerald-300 text-sm font-medium">Enregistrer</button>
                          <button onClick={() => setEditingRule(null)} className="text-white/40 hover:text-white text-sm">Annuler</button>
                        </td>
                      </>
                    ) : (
                      <>
                        <td className="p-4 text-white">{rule.keyword}</td>
                        <td className="p-4 text-white/70">{rule.categoryName}</td>
                        <td className="p-4 text-right space-x-2">
                          <button onClick={() => handleStartEditRule(rule)} className="text-white/40 hover:text-amber-400 transition-colors">
                            <svg className="w-4 h-4 inline" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                              <path strokeLinecap="round" strokeLinejoin="round" d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" />
                            </svg>
                          </button>
                          {ruleDeleteConfirm === rule.id ? (
                            <>
                              <button onClick={() => handleDeleteRule(rule.id)} className="text-red-400 hover:text-red-300 text-sm font-medium">Confirmer</button>
                              <button onClick={() => setRuleDeleteConfirm(null)} className="text-white/40 hover:text-white text-sm">Annuler</button>
                            </>
                          ) : (
                            <button onClick={() => setRuleDeleteConfirm(rule.id)} className="text-white/40 hover:text-red-400 transition-colors">
                              <svg className="w-4 h-4 inline" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                              </svg>
                            </button>
                          )}
                        </td>
                      </>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>

      {/* Modal connexion bancaire */}
      {showConnectModal && (
        <div
          role="dialog"
          aria-modal="true"
          className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center"
          onClick={() => setShowConnectModal(false)}
        >
          <div
            className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-8 w-full max-w-2xl max-h-[80vh] flex flex-col"
            onClick={(e) => e.stopPropagation()}
          >
            <h3 className="text-xl font-bold text-white mb-6">Connecter une banque</h3>

            <div className="flex gap-4 mb-4">
              <select
                value={selectedCountry}
                onChange={(e) => { setSelectedCountry(e.target.value); setInstitutionSearch(''); }}
                className="px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
              >
                {EU_COUNTRIES.map((c) => (
                  <option key={c.code} value={c.code}>{c.label}</option>
                ))}
              </select>
              <input
                type="text"
                value={institutionSearch}
                onChange={(e) => setInstitutionSearch(e.target.value)}
                placeholder="Rechercher une banque..."
                className="flex-1 px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
              />
            </div>

            <div className="flex-1 overflow-y-auto min-h-0">
              {institutions.length === 0 ? (
                <div className="p-8 text-center text-white/30">Chargement des banques...</div>
              ) : filteredInstitutions.length === 0 ? (
                <div className="p-8 text-center text-white/30">Aucune banque trouvée</div>
              ) : (
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
                  {filteredInstitutions.map((inst) => (
                    <button
                      key={inst.id}
                      onClick={() => handleConnect(inst)}
                      disabled={connectingInstitution !== null}
                      className="flex flex-col items-center gap-2 p-4 rounded-xl bg-white/5 border border-white/10 hover:border-amber-500/30 hover:bg-white/10 transition-all duration-200 disabled:opacity-50"
                    >
                      <img src={inst.logo} alt={inst.name} className="w-10 h-10 rounded-lg object-contain" />
                      <span className="text-white/80 text-sm text-center leading-tight">{inst.name}</span>
                      {connectingInstitution === inst.id && (
                        <span className="text-amber-400 text-xs">Redirection...</span>
                      )}
                    </button>
                  ))}
                </div>
              )}
            </div>

            <div className="mt-4 pt-4 border-t border-white/10">
              <button
                onClick={() => setShowConnectModal(false)}
                className="px-6 py-2.5 rounded-xl border border-white/10 text-white/60 hover:text-white hover:bg-white/5 transition-all"
              >
                Fermer
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

// Composant carte de connexion bancaire
interface ConnectionCardProps {
  connection: BankConnection;
  expanded: boolean;
  onToggleExpand: () => void;
  onSync: () => void;
  syncing: boolean;
  deleteConfirm: boolean;
  onDeleteRequest: () => void;
  onDeleteConfirm: () => void;
  onDeleteCancel: () => void;
  onToggleAccount: (account: BankAccount) => void;
}

const ConnectionCard = ({
  connection, expanded, onToggleExpand, onSync, syncing,
  deleteConfirm, onDeleteRequest, onDeleteConfirm, onDeleteCancel, onToggleAccount,
}: ConnectionCardProps) => (
  <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 overflow-hidden">
    <div className="p-5 flex items-center justify-between">
      <div className="flex items-center gap-4">
        <img src={connection.institutionLogo} alt={connection.institutionName} className="w-10 h-10 rounded-lg object-contain" />
        <div>
          <div className="flex items-center gap-3">
            <span className="text-white font-medium">{connection.institutionName}</span>
            <span className={`px-2.5 py-0.5 rounded-full text-xs font-medium border ${statusColor(connection.status)}`}>
              {statusLabel(connection.status)}
            </span>
          </div>
          <p className="text-white/40 text-sm mt-0.5">
            {connection.lastSyncAt
              ? `Dernière sync : ${new Date(connection.lastSyncAt).toLocaleDateString('fr-FR', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' })}`
              : 'Jamais synchronisée'}
          </p>
        </div>
      </div>
      <div className="flex items-center gap-2">
        <button
          onClick={onSync}
          disabled={syncing}
          className="px-4 py-2 rounded-xl text-sm text-white/60 hover:text-amber-400 hover:bg-amber-500/10 border border-white/10 transition-all disabled:opacity-50"
        >
          {syncing ? 'Sync...' : 'Synchroniser'}
        </button>
        {deleteConfirm ? (
          <>
            <button onClick={onDeleteConfirm} className="text-red-400 hover:text-red-300 text-sm font-medium px-3 py-2">Confirmer</button>
            <button onClick={onDeleteCancel} className="text-white/40 hover:text-white text-sm px-3 py-2">Annuler</button>
          </>
        ) : (
          <button
            onClick={onDeleteRequest}
            className="px-4 py-2 rounded-xl text-sm text-white/40 hover:text-red-400 hover:bg-red-500/10 border border-white/10 transition-all"
          >
            Supprimer
          </button>
        )}
        <button
          onClick={onToggleExpand}
          className="px-3 py-2 text-white/40 hover:text-white transition-colors"
        >
          <svg className={`w-5 h-5 transition-transform duration-200 ${expanded ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
          </svg>
        </button>
      </div>
    </div>

    {/* Liste des comptes (expandable) */}
    {expanded && connection.accounts.length > 0 && (
      <div className="border-t border-white/10 px-5 py-3 space-y-2">
        {connection.accounts.map((account) => (
          <div key={account.id} className="flex items-center justify-between py-2 px-3 rounded-xl bg-white/5">
            <div className="flex items-center gap-4">
              <div>
                <p className="text-white/80 text-sm font-medium">
                  {account.accountName ?? account.ownerName ?? 'Compte'}
                </p>
                <p className="text-white/40 text-xs">
                  {account.iban ?? 'IBAN masqué'} · {account.currency}
                </p>
              </div>
            </div>
            <button
              onClick={() => onToggleAccount(account)}
              className={`relative w-11 h-6 rounded-full transition-colors duration-200 ${account.isActive ? 'bg-amber-500' : 'bg-white/20'}`}
            >
              <span className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white transition-transform duration-200 ${account.isActive ? 'translate-x-5' : ''}`} />
            </button>
          </div>
        ))}
      </div>
    )}
  </div>
);

export default Bank;
