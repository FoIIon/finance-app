import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useDashboards } from '../hooks/useDashboards';
import { useInvestmentsQuery } from '../hooks/queries';
import { investmentsApi } from '../api/investments';
import { InvestmentKind, InvestmentUnit } from '../types/investment';
import type { Investment, CreateInvestment, UpdateInvestment } from '../types/investment';
import { formatCurrency } from '../utils/format';
import { useToast } from '../context/ToastContext';

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

const Investments = () => {
  const { currentDashboard } = useDashboards();
  const dashboardId = currentDashboard?.id;
  const { data: investments, isLoading } = useInvestmentsQuery(dashboardId);
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const [form, setForm] = useState<InvestmentForm>(emptyForm);
  const [valuationFor, setValuationFor] = useState<Investment | null>(null);
  const [valuationValue, setValuationValue] = useState('');
  const [valuationDate, setValuationDate] = useState(localIsoDate(new Date()));
  const [deleteConfirm, setDeleteConfirm] = useState<number | null>(null);
  const [editingFor, setEditingFor] = useState<Investment | null>(null);
  const [editForm, setEditForm] = useState<EditForm>(emptyEditForm);

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
    </div>
  );
};

export default Investments;
