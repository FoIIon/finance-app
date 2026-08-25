import { useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useDashboards } from '../hooks/useDashboards';
import {
  useInvestmentsQuery,
  useInvestmentHistoryQuery,
  useInvestmentValuationsQuery,
} from '../hooks/queries';
import { investmentsApi } from '../api/investments';
import { InvestmentKind, InvestmentUnit } from '../types/investment';
import type { Investment, InvestmentValuation, CreateInvestment, UpdateInvestment } from '../types/investment';
import { formatCurrency } from '../utils/format';
import { useToast } from '../hooks/useToast';
import { PortfolioSummary } from '../components/investments/PortfolioSummary';
import { PortfolioChart } from '../components/investments/PortfolioChart';
import { PortfolioPeriodSelector } from '../components/investments/PortfolioPeriodSelector';
import { AllocationCharts } from '../components/investments/AllocationCharts';
import { Sparkline } from '../components/investments/Sparkline';
import { InvestmentDetail } from '../components/investments/InvestmentDetail';
import type { PortfolioPeriod } from '../components/investments/portfolioPeriod';

interface InvestmentForm {
  name: string;
  holder: string;
  kind: number;
  isin: string;
  metalCode: string;
  quantity: string;
  unit: number;
  costBasis: string;
  firstPurchaseDate: string;
}

const emptyForm: InvestmentForm = {
  name: '',
  holder: '',
  kind: InvestmentKind.Security,
  isin: '',
  metalCode: 'XAU',
  quantity: '',
  unit: InvestmentUnit.Share,
  costBasis: '',
  firstPurchaseDate: '',
};

const kindLabels: Record<number, string> = {
  [InvestmentKind.Security]: 'Titre coté',
  [InvestmentKind.Metal]: 'Métal',
  [InvestmentKind.InsuranceContract]: 'Assurance-vie',
};

const unitLabels: Record<number, string> = {
  [InvestmentUnit.Share]: 'part',
  [InvestmentUnit.Gram]: 'g',
  [InvestmentUnit.Ounce]: 'oz',
  [InvestmentUnit.Contract]: 'contrat',
};

interface EditForm {
  name: string;
  holder: string;
  isin: string;
  metalCode: string;
  quantity: string;
  costBasis: string;
  firstPurchaseDate: string;
}

const emptyEditForm: EditForm = {
  name: '',
  holder: '',
  isin: '',
  metalCode: 'XAU',
  quantity: '',
  costBasis: '',
  firstPurchaseDate: '',
};

// Date du jour en calendrier local (pas UTC) : toISOString() convertit en UTC avant de
// tronquer, ce qui décale la date d'un jour pour un utilisateur en avance sur l'UTC (le soir
// en Europe). Calculée à la demande plutôt que figée au chargement du module, pour qu'un
// onglet resté ouvert après minuit ne garde pas la date de la veille comme plafond.
const localIsoDate = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

type Grouping = 'none' | 'holder' | 'kind';

// Sous-totaux d'un groupe : la plus-value et son % ne se calculent que sur les lignes
// valorisées, comparer un investi complet à une valeur partielle serait mensonger.
const groupSubtotals = (rows: Investment[]) => {
  const invested = rows.reduce((s, r) => s + r.costBasis, 0);
  const valued = rows.filter((r) => r.marketValue != null);
  const value = valued.reduce((s, r) => s + (r.marketValue ?? 0), 0);
  const investedValued = valued.reduce((s, r) => s + r.costBasis, 0);
  const gain = value - investedValued;
  const pct = investedValued > 0 ? (gain / investedValued) * 100 : null;
  return { invested, value, gain, pct, hasValued: valued.length > 0 };
};

const Investments = () => {
  const { currentDashboard } = useDashboards();
  const dashboardId = currentDashboard?.id;
  const { data: investments, isLoading } = useInvestmentsQuery(dashboardId);
  const { data: history, isLoading: historyLoading } = useInvestmentHistoryQuery(dashboardId);
  const { data: allValuations } = useInvestmentValuationsQuery(dashboardId);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const [form, setForm] = useState<InvestmentForm>(emptyForm);
  const [valuationFor, setValuationFor] = useState<Investment | null>(null);
  const [valuationValue, setValuationValue] = useState('');
  const [valuationDate, setValuationDate] = useState(localIsoDate(new Date()));
  const [deleteConfirm, setDeleteConfirm] = useState<number | null>(null);
  const [editingFor, setEditingFor] = useState<Investment | null>(null);
  const [editForm, setEditForm] = useState<EditForm>(emptyEditForm);
  const [period, setPeriod] = useState<PortfolioPeriod>('MAX');
  const [grouping, setGrouping] = useState<Grouping>('none');
  const [detailFor, setDetailFor] = useState<Investment | null>(null);
  const [importing, setImporting] = useState(false);

  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['investments', dashboardId] });
    queryClient.invalidateQueries({ queryKey: ['investment-history', dashboardId] });
    queryClient.invalidateQueries({ queryKey: ['investment-valuations', dashboardId] });
  };

  // Une seule requête pour toutes les sparklines : regroupement par ligne côté client.
  const valuationsByLine = useMemo(() => {
    const map = new Map<number, InvestmentValuation[]>();
    for (const v of allValuations ?? []) {
      const arr = map.get(v.investmentId);
      if (arr) arr.push(v);
      else map.set(v.investmentId, [v]);
    }
    return map;
  }, [allValuations]);

  const groups = useMemo(() => {
    if (grouping === 'none') return null;
    const map = new Map<string, Investment[]>();
    for (const i of investments ?? []) {
      const key = grouping === 'holder' ? i.holder : kindLabels[i.kind];
      const arr = map.get(key);
      if (arr) arr.push(i);
      else map.set(key, [i]);
    }
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b, 'fr'));
  }, [investments, grouping]);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dashboardId) return;

    const isContract = form.kind === InvestmentKind.InsuranceContract;
    const payload: CreateInvestment = {
      dashboardId,
      name: form.name,
      holder: form.holder,
      kind: form.kind as CreateInvestment['kind'],
      isin: form.kind === InvestmentKind.Security ? form.isin || null : null,
      metalCode: form.kind === InvestmentKind.Metal ? form.metalCode : null,
      quantity: isContract ? 1 : parseFloat(form.quantity || '0'),
      unit: (isContract ? InvestmentUnit.Contract : form.unit) as CreateInvestment['unit'],
      costBasis: parseFloat(form.costBasis || '0'),
      firstPurchaseDate: form.firstPurchaseDate || null,
    };

    try {
      await investmentsApi.create(payload);
      setForm(emptyForm);
      refresh();
      showToast('Ligne ajoutée', 'success');
    } catch {
      showToast("Impossible d'ajouter la ligne", 'error');
    }
  };

  const handleValuation = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!valuationFor) return;

    try {
      await investmentsApi.addValuation(valuationFor.id, {
        asOf: valuationDate,
        marketValue: parseFloat(valuationValue || '0'),
      });
      setValuationFor(null);
      setValuationValue('');
      refresh();
      showToast('Valorisation enregistrée', 'success');
    } catch {
      showToast("Impossible d'enregistrer la valorisation", 'error');
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await investmentsApi.delete(id);
      setDeleteConfirm(null);
      refresh();
      showToast('Ligne supprimée', 'success');
    } catch {
      showToast('Impossible de supprimer la ligne', 'error');
    }
  };

  const openEdit = (i: Investment) => {
    setValuationFor(null);
    setEditingFor(i);
    setEditForm({
      name: i.name,
      holder: i.holder,
      isin: i.isin ?? '',
      metalCode: i.metalCode ?? 'XAU',
      quantity: i.kind === InvestmentKind.InsuranceContract ? '' : String(i.quantity),
      costBasis: String(i.costBasis),
      firstPurchaseDate: i.firstPurchaseDate ? i.firstPurchaseDate.slice(0, 10) : '',
    });
  };

  const handleEdit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingFor) return;

    const isContract = editingFor.kind === InvestmentKind.InsuranceContract;
    const payload: UpdateInvestment = {
      name: editForm.name,
      holder: editForm.holder,
      isin: editingFor.kind === InvestmentKind.Security ? editForm.isin || null : null,
      metalCode: editingFor.kind === InvestmentKind.Metal ? editForm.metalCode : null,
      costBasis: parseFloat(editForm.costBasis || '0'),
      firstPurchaseDate: editForm.firstPurchaseDate || null,
      ...(isContract ? {} : { quantity: parseFloat(editForm.quantity || '0') }),
    };

    try {
      await investmentsApi.update(editingFor.id, payload);
      setEditingFor(null);
      refresh();
      showToast('Ligne modifiée', 'success');
    } catch {
      showToast('Impossible de modifier la ligne', 'error');
    }
  };

  const handleImportTradeRepublic = async () => {
    if (!dashboardId) return;
    setImporting(true);
    try {
      const { data } = await investmentsApi.importTradeRepublic(dashboardId);
      refresh();
      showToast(
        `Trade Republic : ${data.total} lignes, ${data.created} ajoutées, ${data.valued} valorisées`,
        'success',
      );
    } catch (err: unknown) {
      const serverMessage = (err as { response?: { data?: string } })?.response?.data;
      showToast(
        typeof serverMessage === 'string' ? serverMessage : "Import Trade Republic impossible",
        'error',
      );
    } finally {
      setImporting(false);
    }
  };

  if (isLoading) return <div className="p-6 text-white/60">Chargement...</div>;

  const renderRow = (i: Investment) => (
    <tr key={i.id} className="border-b border-white/5 text-white/90">
      <td className="p-3">
        <button
          type="button"
          onClick={() => setDetailFor(i)}
          className="text-white hover:text-indigo-300 hover:underline underline-offset-2 text-left"
        >
          {i.name}
        </button>
        <span className="text-white/40 ml-2">{kindLabels[i.kind]}</span>
      </td>
      <td className="p-3">{i.holder}</td>
      <td className="p-3">
        <Sparkline valuations={valuationsByLine.get(i.id) ?? []} costBasis={i.costBasis} />
      </td>
      <td className="p-3 text-right">
        {i.kind === InvestmentKind.InsuranceContract ? '—' : `${i.quantity} ${unitLabels[i.unit]}`}
      </td>
      <td className="p-3 text-right">{i.unitCost != null ? formatCurrency(i.unitCost) : '—'}</td>
      <td className="p-3 text-right">{formatCurrency(i.costBasis)}</td>
      <td className={`p-3 text-right ${i.isStale ? 'text-white/40' : ''}`}>
        {i.marketValue != null ? formatCurrency(i.marketValue) : '—'}
        {i.valuationAsOf && (
          <div className="text-xs text-white/40">
            au {new Date(i.valuationAsOf).toLocaleDateString('fr-BE')}
          </div>
        )}
      </td>
      <td className={`p-3 text-right ${(i.gainAmount ?? 0) >= 0 ? 'text-emerald-400' : 'text-rose-400'}`}>
        {i.gainAmount != null ? formatCurrency(i.gainAmount) : '—'}
        {i.gainPercent != null && (
          <div className="text-xs opacity-70">{i.gainPercent.toFixed(1)} %</div>
        )}
      </td>
      <td className="p-3 text-right">
        {i.annualizedReturn != null ? (
          <span title="Approximatif, calculé sur la date d'entrée">
            {i.annualizedReturn.toFixed(1)} % / an
          </span>
        ) : (
          <span className="text-white/30" title="Renseigne une date d'entrée pour obtenir un rendement">
            —
          </span>
        )}
      </td>
      <td className="p-3 text-right whitespace-nowrap">
        <button
          onClick={() => { setEditingFor(null); setValuationFor(i); setValuationValue(''); }}
          className="text-indigo-300 hover:text-indigo-200 mr-3"
        >
          Valoriser
        </button>
        <button
          onClick={() => openEdit(i)}
          className="text-indigo-300 hover:text-indigo-200 mr-3"
        >
          Modifier
        </button>
        {deleteConfirm === i.id ? (
          <>
            <button onClick={() => handleDelete(i.id)} className="text-rose-400 hover:text-rose-300 text-xs font-medium mr-2">
              Confirmer
            </button>
            <button onClick={() => setDeleteConfirm(null)} className="text-white/40 hover:text-white text-xs">
              Annuler
            </button>
          </>
        ) : (
          <button onClick={() => setDeleteConfirm(i.id)} className="text-white/40 hover:text-rose-400">
            Supprimer
          </button>
        )}
      </td>
    </tr>
  );

  const renderGroupHeader = (name: string, rows: Investment[]) => {
    const st = groupSubtotals(rows);
    return (
      <tr key={`group-${name}`} className="border-b border-white/10 bg-white/5 text-white/80">
        <td className="p-3 font-semibold" colSpan={5}>
          {name} <span className="text-white/40 font-normal">({rows.length})</span>
        </td>
        <td className="p-3 text-right font-medium">{formatCurrency(st.invested)}</td>
        <td className="p-3 text-right font-medium">{st.hasValued ? formatCurrency(st.value) : '—'}</td>
        <td className={`p-3 text-right font-medium ${st.hasValued ? (st.gain >= 0 ? 'text-emerald-400' : 'text-rose-400') : 'text-white/30'}`}>
          {st.hasValued ? (
            <>
              {formatCurrency(st.gain)}
              <div className="text-xs opacity-70">{st.pct != null ? `${st.pct.toFixed(1)} %` : '—'}</div>
            </>
          ) : (
            '—'
          )}
        </td>
        <td className="p-3" colSpan={2}></td>
      </tr>
    );
  };

  return (
    <div className="p-6 space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold text-white">Investissements</h1>
        <div className="flex flex-wrap items-center gap-3">
          <button
            type="button"
            onClick={handleImportTradeRepublic}
            disabled={importing}
            title="Importe les positions et le cours du jour depuis Trade Republic. Nécessite une connexion Trade Republic récente dans Banques."
            className="rounded-lg border border-white/10 bg-white/5 hover:bg-white/10 px-3 py-2 text-sm text-white/80 disabled:opacity-50"
          >
            {importing ? 'Import en cours...' : 'Importer Trade Republic'}
          </button>
          <PortfolioPeriodSelector value={period} onChange={setPeriod} />
        </div>
      </div>

      <PortfolioSummary investments={investments ?? []} history={history ?? []} period={period} />

      <PortfolioChart history={history ?? []} period={period} isLoading={historyLoading} />

      <AllocationCharts investments={investments ?? []} />

      <form onSubmit={handleCreate} className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-4 grid gap-3 md:grid-cols-7">
        <input
          required
          placeholder="Nom"
          className="bg-white/5 rounded-lg px-3 py-2 text-white md:col-span-2"
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
        />
        <input
          required
          list="holders"
          placeholder="Titulaire"
          className="bg-white/5 rounded-lg px-3 py-2 text-white"
          value={form.holder}
          onChange={(e) => setForm({ ...form, holder: e.target.value })}
        />
        <datalist id="holders">
          {[...new Set((investments ?? []).map((i) => i.holder))].map((h) => (
            <option key={h} value={h} />
          ))}
        </datalist>
        <select
          className="bg-white/5 rounded-lg px-3 py-2 text-white"
          value={form.kind}
          onChange={(e) => {
            // L'unité suit la nature de l'actif : une ligne d'or saisie en « part »
            // rendrait la conversion du cours spot impossible au lot suivant.
            const kind = Number(e.target.value);
            const unit =
              kind === InvestmentKind.Metal
                ? InvestmentUnit.Gram
                : kind === InvestmentKind.InsuranceContract
                  ? InvestmentUnit.Contract
                  : InvestmentUnit.Share;
            const metalCode = kind === InvestmentKind.Metal ? 'XAU' : form.metalCode;
            setForm({ ...form, kind, unit, metalCode });
          }}
        >
          {Object.entries(kindLabels).map(([value, label]) => (
            <option key={value} value={value}>{label}</option>
          ))}
        </select>
        {form.kind === InvestmentKind.Security && (
          <input
            placeholder="ISIN"
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={form.isin}
            onChange={(e) => setForm({ ...form, isin: e.target.value })}
          />
        )}
        {form.kind === InvestmentKind.Metal && (
          <select
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={form.metalCode}
            onChange={(e) => setForm({ ...form, metalCode: e.target.value })}
          >
            <option value="XAU">Or</option>
            <option value="XAG">Argent</option>
          </select>
        )}
        {form.kind === InvestmentKind.Metal && (
          <select
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={form.unit}
            onChange={(e) => setForm({ ...form, unit: Number(e.target.value) })}
          >
            <option value={InvestmentUnit.Gram}>gramme</option>
            <option value={InvestmentUnit.Ounce}>once</option>
          </select>
        )}
        {form.kind !== InvestmentKind.InsuranceContract && (
          <input
            required
            type="number"
            step="0.000001"
            placeholder="Quantité"
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={form.quantity}
            onChange={(e) => setForm({ ...form, quantity: e.target.value })}
          />
        )}
        <input
          required
          type="number"
          step="0.01"
          placeholder="Montant investi"
          className="bg-white/5 rounded-lg px-3 py-2 text-white"
          value={form.costBasis}
          onChange={(e) => setForm({ ...form, costBasis: e.target.value })}
        />
        <input
          type="date"
          title="Date d'entrée, nécessaire pour afficher un rendement annualisé"
          className="bg-white/5 rounded-lg px-3 py-2 text-white"
          value={form.firstPurchaseDate}
          onChange={(e) => setForm({ ...form, firstPurchaseDate: e.target.value })}
        />
        <button type="submit" className="bg-indigo-500 hover:bg-indigo-400 rounded-lg px-4 py-2 text-white font-medium">
          Ajouter
        </button>
      </form>

      <div className="flex justify-end">
        <label className="flex items-center gap-2 text-sm text-white/50">
          Grouper par
          <select
            aria-label="Groupement"
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={grouping}
            onChange={(e) => setGrouping(e.target.value as Grouping)}
          >
            <option value="none">Aucun</option>
            <option value="holder">Titulaire</option>
            <option value="kind">Type</option>
          </select>
        </label>
      </div>

      <div className="bg-[#1a1a3e] rounded-2xl border border-white/10 overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="text-white/50 border-b border-white/10">
            <tr>
              <th className="text-left p-3">Ligne</th>
              <th className="text-left p-3">Titulaire</th>
              <th className="text-left p-3">Tendance</th>
              <th className="text-right p-3">Quantité</th>
              <th className="text-right p-3">PRU</th>
              <th className="text-right p-3">Investi</th>
              <th className="text-right p-3">Valeur</th>
              <th className="text-right p-3">Plus-value</th>
              <th className="text-right p-3">Rendement</th>
              <th className="p-3"></th>
            </tr>
          </thead>
          <tbody>
            {groups
              ? groups.map(([name, rows]) => (
                  [renderGroupHeader(name, rows), ...rows.map(renderRow)]
                ))
              : (investments ?? []).map(renderRow)}
          </tbody>
        </table>
      </div>

      {valuationFor && (
        <form onSubmit={handleValuation} className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-4 flex flex-wrap gap-3 items-center">
          <span className="text-white">Valoriser {valuationFor.name}</span>
          <input
            required
            type="number"
            step="0.01"
            placeholder="Valeur actuelle"
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={valuationValue}
            onChange={(e) => setValuationValue(e.target.value)}
          />
          <input
            required
            type="date"
            title="Date du relevé, pas date de saisie"
            max={localIsoDate(new Date())}
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={valuationDate}
            onChange={(e) => setValuationDate(e.target.value)}
          />
          <button type="submit" className="bg-indigo-500 hover:bg-indigo-400 rounded-lg px-4 py-2 text-white">
            Enregistrer
          </button>
          <button type="button" onClick={() => setValuationFor(null)} className="text-white/50 hover:text-white">
            Annuler
          </button>
        </form>
      )}

      {editingFor && (
        <form onSubmit={handleEdit} className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-4 flex flex-wrap gap-3 items-center">
          <span className="text-white">Modifier {editingFor.name}</span>
          <input
            required
            placeholder="Nom"
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={editForm.name}
            onChange={(e) => setEditForm({ ...editForm, name: e.target.value })}
          />
          <input
            required
            list="holders"
            placeholder="Titulaire"
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={editForm.holder}
            onChange={(e) => setEditForm({ ...editForm, holder: e.target.value })}
          />
          {editingFor.kind === InvestmentKind.Security && (
            <input
              placeholder="ISIN"
              className="bg-white/5 rounded-lg px-3 py-2 text-white"
              value={editForm.isin}
              onChange={(e) => setEditForm({ ...editForm, isin: e.target.value })}
            />
          )}
          {editingFor.kind === InvestmentKind.Metal && (
            <select
              className="bg-white/5 rounded-lg px-3 py-2 text-white"
              value={editForm.metalCode}
              onChange={(e) => setEditForm({ ...editForm, metalCode: e.target.value })}
            >
              <option value="XAU">Or</option>
              <option value="XAG">Argent</option>
            </select>
          )}
          {editingFor.kind !== InvestmentKind.InsuranceContract && (
            <input
              required
              type="number"
              step="0.000001"
              placeholder="Quantité"
              className="bg-white/5 rounded-lg px-3 py-2 text-white"
              value={editForm.quantity}
              onChange={(e) => setEditForm({ ...editForm, quantity: e.target.value })}
            />
          )}
          <input
            required
            type="number"
            step="0.01"
            placeholder="Montant investi"
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={editForm.costBasis}
            onChange={(e) => setEditForm({ ...editForm, costBasis: e.target.value })}
          />
          <input
            type="date"
            title="Date d'entrée, nécessaire pour afficher un rendement annualisé"
            className="bg-white/5 rounded-lg px-3 py-2 text-white"
            value={editForm.firstPurchaseDate}
            onChange={(e) => setEditForm({ ...editForm, firstPurchaseDate: e.target.value })}
          />
          <button type="submit" className="bg-indigo-500 hover:bg-indigo-400 rounded-lg px-4 py-2 text-white">
            Enregistrer
          </button>
          <button type="button" onClick={() => setEditingFor(null)} className="text-white/50 hover:text-white">
            Annuler
          </button>
        </form>
      )}

      {detailFor && <InvestmentDetail investment={detailFor} onClose={() => setDetailFor(null)} />}
    </div>
  );
};

export default Investments;
