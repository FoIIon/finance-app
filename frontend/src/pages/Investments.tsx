import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useDashboards } from '../hooks/useDashboards';
import { useInvestmentsQuery } from '../hooks/queries';
import { investmentsApi } from '../api/investments';
import { InvestmentKind, InvestmentUnit } from '../types/investment';
import type { Investment, CreateInvestment } from '../types/investment';
import { formatCurrency } from '../utils/format';
import { useToast } from '../context/ToastContext';

interface InvestmentForm {
  name: string;
  holder: string;
  kind: number;
  quantity: string;
  unit: number;
  costBasis: string;
  firstPurchaseDate: string;
}

const emptyForm: InvestmentForm = {
  name: '',
  holder: '',
  kind: InvestmentKind.Security,
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

const Investments = () => {
  const { currentDashboard } = useDashboards();
  const dashboardId = currentDashboard?.id;
  const { data: investments, isLoading } = useInvestmentsQuery(dashboardId);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const [form, setForm] = useState<InvestmentForm>(emptyForm);
  const [valuationFor, setValuationFor] = useState<Investment | null>(null);
  const [valuationValue, setValuationValue] = useState('');
  const [valuationDate, setValuationDate] = useState(new Date().toISOString().slice(0, 10));
  const [deleteConfirm, setDeleteConfirm] = useState<number | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['investments', dashboardId] });

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dashboardId) return;

    const isContract = form.kind === InvestmentKind.InsuranceContract;
    const payload: CreateInvestment = {
      dashboardId,
      name: form.name,
      holder: form.holder,
      kind: form.kind as CreateInvestment['kind'],
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

  if (isLoading) return <div className="p-6 text-white/60">Chargement...</div>;

  return (
    <div className="p-6 space-y-6">
      <h1 className="text-2xl font-semibold text-white">Investissements</h1>

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
            setForm({ ...form, kind, unit });
          }}
        >
          {Object.entries(kindLabels).map(([value, label]) => (
            <option key={value} value={value}>{label}</option>
          ))}
        </select>
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

      <div className="bg-[#1a1a3e] rounded-2xl border border-white/10 overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="text-white/50 border-b border-white/10">
            <tr>
              <th className="text-left p-3">Ligne</th>
              <th className="text-left p-3">Titulaire</th>
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
            {(investments ?? []).map((i) => (
              <tr key={i.id} className="border-b border-white/5 text-white/90">
                <td className="p-3">
                  {i.name}
                  <span className="text-white/40 ml-2">{kindLabels[i.kind]}</span>
                </td>
                <td className="p-3">{i.holder}</td>
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
                    onClick={() => { setValuationFor(i); setValuationValue(''); }}
                    className="text-indigo-300 hover:text-indigo-200 mr-3"
                  >
                    Valoriser
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
            ))}
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
    </div>
  );
};

export default Investments;
