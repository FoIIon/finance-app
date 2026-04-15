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

// Logo officiel Trade Republic (SVG inline, fond noir, icône blanche)
const TR_LOGO = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1552 1319'%3E%3Crect width='1552' height='1319' fill='%23000000'/%3E%3Cpath fill='%23ffffff' fill-rule='evenodd' d='m1551 659.5q0-2-1-3v-2c-5-22.9-25.9-35.9-46.9-30.9l-553.5 141.7c-17 5-34 4-50.9-2l-250.4-87.8c-15.9-6-33.9-6-50.8-2l-567.6 146.6c-16.9 4-29.9 21-29.9 40v422q0 1 1 2v3c5 21.9 25.9 35.9 46.9 29.9l548.6-141.7q6-2 12.9-2 7-1 13-1 7 1 13 2 6.9 1 12.9 3l249.4 88.8q6 2 12 3 6.9 2 12.9 2h13q7-1 13-3l571.5-146.6q7-2 12-6 5.9-4 9.9-9 4-5 6-12 2-6 2-12.9l1-1zm-1-619.7v-6.9c-5-22-26.9-36-46.9-31l-552.5 142.7c-17 4-35 3-51.9-3l-249.4-87.8c-16.9-6-34.9-6-51.8-2l-567.6 146.7c-16.9 4-29.9 20.9-29.9 39.9v420.1q0 1 1 2v4.9c5 22 25.9 36 46.9 31l548.6-141.7q6.9-2 12.9-3h13q7 0 13 1 6.9 2 12.9 4l249.4 87.8q7 2 13 3 5.9 2 12.9 2h13q7-1 13-3l570.5-146.7q7-2 13-6 4.9-3 8.9-9 4-4.9 7-11.9 2-6 2-13v-417.1q0-2-1-3z'/%3E%3C/svg%3E";

// Entrée synthétique Trade Republic injectée dans la liste allemande
const TR_INSTITUTION = {
  id: 'TRADE_REPUBLIC',
  name: 'Trade Republic',
  logo: TR_LOGO,
  countries: ['DE'],
};

type TrStep = null | 'credentials' | 'twoFactor' | 'success';

const statusLabel = (status: BankConnectionStatus) => {
  switch (status) {
    case BankConnectionStatus.Linked: return 'Connectée';
    case BankConnectionStatus.Expired: return 'Expirée';
    case BankConnectionStatus.Error: return 'Erreur';
    case BankConnectionStatus.PendingTwoFactor: return 'En attente 2FA';
  }
};

const statusColor = (status: BankConnectionStatus) => {
  switch (status) {
    case BankConnectionStatus.Linked: return 'bg-emerald-500/20 text-emerald-400 border-emerald-500/30';
    case BankConnectionStatus.Expired: return 'bg-orange-500/20 text-orange-400 border-orange-500/30';
    case BankConnectionStatus.Error: return 'bg-red-500/20 text-red-400 border-red-500/30';
    case BankConnectionStatus.PendingTwoFactor: return 'bg-yellow-500/20 text-yellow-400 border-yellow-500/30';
  }
};

const Bank = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const {
    connections, institutions, categoryRules, loading, error,
    fetchInstitutions, connectBank, handleCallback,
    deleteConnection, syncConnection, updateAccount,
    fetchCategoryRules, createCategoryRule, updateCategoryRule, deleteCategoryRule,
    tradeRepublicLogin, tradeRepublicVerify,
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

  // État du flow Trade Republic dans le modal
  const [trStep, setTrStep] = useState<TrStep>(null);
  const [trPhoneNumber, setTrPhoneNumber] = useState('');
  const [trPin, setTrPin] = useState('');
  const [trCode, setTrCode] = useState('');
  const [trConnectionId, setTrConnectionId] = useState<number | null>(null);
  const [trLoading, setTrLoading] = useState(false);
  const [trError, setTrError] = useState<string | null>(null);

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

  const handleCloseModal = () => {
    setShowConnectModal(false);
    // Réinitialiser le flow Trade Republic
    setTrStep(null);
    setTrPhoneNumber('');
    setTrPin('');
    setTrCode('');
    setTrConnectionId(null);
    setTrError(null);
  };

  const handleConnect = async (institution: { id: string; name: string; logo: string }) => {
    if (institution.id === 'TRADE_REPUBLIC') {
      setTrStep('credentials');
      return;
    }
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

  const handleTrLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setTrLoading(true);
    setTrError(null);
    try {
      const id = await tradeRepublicLogin({ phoneNumber: trPhoneNumber, pin: trPin });
      setTrConnectionId(id);
      setTrStep('twoFactor');
    } catch (err: unknown) {
      const serverMessage = (err as { response?: { data?: string } })?.response?.data;
      setTrError(typeof serverMessage === 'string' ? serverMessage : 'Erreur de connexion à Trade Republic.');
    } finally {
      setTrLoading(false);
    }
  };

  const handleTrVerify = async (e: React.FormEvent) => {
    e.preventDefault();
    if (trConnectionId === null) return;
    setTrLoading(true);
    setTrError(null);
    try {
      await tradeRepublicVerify({ connectionId: trConnectionId, code: trCode });
      setTrStep('success');
    } catch (err: unknown) {
      const serverMessage = (err as { response?: { data?: string } })?.response?.data;
      setTrError(typeof serverMessage === 'string' ? serverMessage : 'Code invalide ou session expirée. Veuillez recommencer.');
    } finally {
      setTrLoading(false);
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

  // Ouvre le modal TR directement à l'étape credentials pour re-authentifier
  const handleReauth = () => {
    setTrStep('credentials');
    setTrError(null);
    setTrCode('');
    setTrConnectionId(null);
    setShowConnectModal(true);
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

  // Trade Republic injecté en tête de liste quand l'Allemagne est sélectionnée
  const allInstitutions = selectedCountry === 'DE'
    ? [TR_INSTITUTION, ...institutions]
    : institutions;

  const filteredInstitutions = allInstitutions.filter((i) =>
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
              onReauth={conn.institutionId === 'trade-republic' && conn.status === BankConnectionStatus.Error ? handleReauth : undefined}
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
          onClick={handleCloseModal}
        >
          <div
            className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-8 w-full max-w-2xl max-h-[80vh] flex flex-col"
            onClick={(e) => e.stopPropagation()}
          >
            {/* En-tête du modal */}
            <div className="flex items-center gap-3 mb-6">
              {trStep !== null && trStep !== 'success' && (
                <button
                  onClick={() => setTrStep(null)}
                  className="text-white/40 hover:text-white transition-colors"
                >
                  <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M15 19l-7-7 7-7" />
                  </svg>
                </button>
              )}
              <h3 className="text-xl font-bold text-white">
                {trStep ? 'Connecter Trade Republic' : 'Connecter une banque'}
              </h3>
            </div>

            {/* Contenu : liste des banques */}
            {trStep === null && (
              <>
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
                  {institutions.length === 0 && selectedCountry !== 'DE' ? (
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
                    onClick={handleCloseModal}
                    className="px-6 py-2.5 rounded-xl border border-white/10 text-white/60 hover:text-white hover:bg-white/5 transition-all"
                  >
                    Fermer
                  </button>
                </div>
              </>
            )}

            {/* Contenu : formulaire identifiants Trade Republic */}
            {trStep === 'credentials' && (
              <form onSubmit={handleTrLogin} className="space-y-4">
                <p className="text-white/60 text-sm">
                  Entrez vos identifiants Trade Republic pour lier votre compte.
                </p>
                {trError && (
                  <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-sm">
                    {trError}
                  </div>
                )}
                <div>
                  <label className="block text-sm text-white/60 mb-1">Numéro de téléphone</label>
                  <input
                    type="tel"
                    value={trPhoneNumber}
                    onChange={(e) => setTrPhoneNumber(e.target.value)}
                    placeholder="+33612345678"
                    required
                    autoFocus
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                  />
                </div>
                <div>
                  <label className="block text-sm text-white/60 mb-1">PIN</label>
                  <input
                    type="password"
                    value={trPin}
                    onChange={(e) => setTrPin(e.target.value)}
                    placeholder="****"
                    required
                    maxLength={4}
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                  />
                </div>
                <button
                  type="submit"
                  disabled={trLoading}
                  className="w-full px-5 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200 disabled:opacity-50"
                >
                  {trLoading ? 'Connexion...' : 'Se connecter'}
                </button>
              </form>
            )}

            {/* Contenu : code 2FA Trade Republic */}
            {trStep === 'twoFactor' && (
              <form onSubmit={handleTrVerify} className="space-y-4">
                <p className="text-white/60 text-sm">
                  Confirmez la notification push sur votre téléphone, puis entrez le code de vérification.
                </p>
                {trError && (
                  <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-sm">
                    {trError}
                  </div>
                )}
                <div>
                  <label className="block text-sm text-white/60 mb-1">Code de vérification</label>
                  <input
                    type="text"
                    value={trCode}
                    onChange={(e) => setTrCode(e.target.value)}
                    placeholder="1234"
                    required
                    maxLength={4}
                    autoFocus
                    className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white text-center text-2xl tracking-[0.5em] focus:outline-none focus:border-amber-500/50"
                  />
                </div>
                <button
                  type="submit"
                  disabled={trLoading}
                  className="w-full px-5 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200 disabled:opacity-50"
                >
                  {trLoading ? 'Vérification...' : 'Vérifier'}
                </button>
              </form>
            )}

            {/* Contenu : succès Trade Republic */}
            {trStep === 'success' && (
              <div className="text-center space-y-4">
                <div className="w-16 h-16 mx-auto rounded-full bg-emerald-500/20 flex items-center justify-center">
                  <svg className="w-8 h-8 text-emerald-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                  </svg>
                </div>
                <h3 className="text-xl font-bold text-white">Compte lié avec succès</h3>
                <p className="text-white/60 text-sm">
                  Votre compte Trade Republic est maintenant connecté.
                </p>
                <button
                  onClick={handleCloseModal}
                  className="px-6 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200"
                >
                  Fermer
                </button>
              </div>
            )}
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
  onReauth?: () => void;
}

const ConnectionCard = ({
  connection, expanded, onToggleExpand, onSync, syncing,
  deleteConfirm, onDeleteRequest, onDeleteConfirm, onDeleteCancel, onToggleAccount, onReauth,
}: ConnectionCardProps) => (
  <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 overflow-hidden">
    <div className="p-5 flex items-center justify-between">
      <div className="flex items-center gap-4">
        <img
          src={connection.institutionLogo || (connection.institutionId === 'trade-republic' ? TR_LOGO : '')}
          alt={connection.institutionName}
          className="w-10 h-10 rounded-lg object-contain"
        />
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
        {onReauth && (
          <button
            onClick={onReauth}
            className="px-4 py-2 rounded-xl text-sm text-amber-400 hover:text-amber-300 hover:bg-amber-500/10 border border-amber-500/30 transition-all"
          >
            Re-authentifier
          </button>
        )}
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
