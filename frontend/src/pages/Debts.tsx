import { useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useDashboards } from '../hooks/useDashboards';
import {
  useLoansQuery,
  useDebtSummaryQuery,
  useLoanScheduleQuery,
  useAccountBalancesQuery,
  useInvestmentsQuery,
} from '../hooks/queries';
import { loansApi } from '../api/loans';
import { LoanKind } from '../types/loan';
import type { Loan, CreateLoan } from '../types/loan';
import { formatCurrency, formatPercent } from '../utils/format';
import { useToast } from '../hooks/useToast';

const kindLabels: Record<number, string> = {
  [LoanKind.Mortgage]: 'Crédit logement',
  [LoanKind.Family]: 'Prêt familial',
  [LoanKind.Consumer]: 'Crédit à la consommation',
};

const formatDate = (iso: string | null | undefined) =>
  iso ? new Date(iso).toLocaleDateString('fr-BE', { day: '2-digit', month: '2-digit', year: 'numeric' }) : '—';

const formatMonthYear = (iso: string | null | undefined) =>
  iso ? new Date(iso).toLocaleDateString('fr-BE', { month: 'long', year: 'numeric' }) : '—';

/** « 12 ans et 9 mois », comme l'annonce la banque, plutôt qu'un nombre de mois brut. */
const formatDuration = (installments: number) => {
  if (installments <= 0) return 'éteint';
  const years = Math.floor(installments / 12);
  const months = installments % 12;
  const y = years > 0 ? `${years} an${years > 1 ? 's' : ''}` : '';
  const m = months > 0 ? `${months} mois` : '';
  return [y, m].filter(Boolean).join(' et ') || `${installments} mois`;
};

interface LoanForm {
  name: string;
  holder: string;
  kind: number;
  lender: string;
  reference: string;
  initialPrincipal: string;
  annualRatePercent: string;
  monthlyPayment: string;
  anchorDate: string;
  anchorPrincipal: string;
  debitIban: string;
}

const emptyForm: LoanForm = {
  name: '',
  holder: '',
  kind: LoanKind.Mortgage,
  lender: '',
  reference: '',
  initialPrincipal: '',
  annualRatePercent: '',
  monthlyPayment: '',
  anchorDate: '',
  anchorPrincipal: '',
  debitIban: '',
};

const toForm = (l: Loan): LoanForm => ({
  name: l.name,
  holder: l.holder,
  kind: l.kind,
  lender: l.lender ?? '',
  reference: l.reference ?? '',
  initialPrincipal: l.initialPrincipal?.toString() ?? '',
  annualRatePercent: l.annualRatePercent.toString(),
  monthlyPayment: l.monthlyPayment.toString(),
  anchorDate: l.anchorDate.slice(0, 10),
  anchorPrincipal: l.anchorPrincipal.toString(),
  debitIban: l.debitIban ?? '',
});

const cardClass = 'bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10';
const inputClass =
  'w-full bg-white/5 border border-white/10 rounded-lg px-3 py-2 text-sm text-white placeholder-white/30 focus:outline-none focus:border-white/30';
const labelClass = 'block text-xs text-white/50 mb-1';

interface KpiProps {
  label: string;
  value: string;
  hint?: string;
  tone?: 'rose' | 'amber' | 'white' | 'emerald';
}

const Kpi = ({ label, value, hint, tone = 'white' }: KpiProps) => {
  const colors: Record<string, string> = {
    rose: 'text-rose-300',
    amber: 'text-amber-300',
    emerald: 'text-emerald-300',
    white: 'text-white',
  };
  return (
    <div className={`${cardClass} p-4 md:p-5`}>
      <p className="text-xs font-semibold uppercase tracking-wider text-white/50">{label}</p>
      <p
        className={`text-xl md:text-2xl font-bold mt-2 ${colors[tone]}`}
        style={{ fontFamily: "'Space Grotesk', sans-serif" }}
      >
        {value}
      </p>
      {hint && <p className="text-white/40 text-xs mt-1">{hint}</p>}
    </div>
  );
};

interface LoanCardProps {
  loan: Loan;
  onEdit: (loan: Loan) => void;
  onDelete: (loan: Loan) => void;
}

const LoanCard = ({ loan, onEdit, onDelete }: LoanCardProps) => {
  const [showSchedule, setShowSchedule] = useState(false);
  const { data: schedule, isLoading } = useLoanScheduleQuery(showSchedule ? loan.id : undefined, 12);

  const repaid = loan.repaidPercent;

  return (
    <div className={`${cardClass} p-5`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="text-white font-semibold truncate">{loan.name}</h3>
          <p className="text-white/40 text-xs mt-0.5">
            {kindLabels[loan.kind] ?? 'Emprunt'}
            {loan.lender ? ` · ${loan.lender}` : ''}
            {loan.holder ? ` · ${loan.holder}` : ''}
          </p>
        </div>
        <div className="text-right">
          <p
            className="text-2xl font-bold text-rose-300"
            style={{ fontFamily: "'Space Grotesk', sans-serif" }}
          >
            {formatCurrency(loan.remainingPrincipal)}
          </p>
          <p className="text-white/40 text-xs">capital restant dû</p>
        </div>
      </div>

      {repaid != null && (
        <div className="mt-4">
          <div className="flex items-center justify-between text-xs text-white/50 mb-1">
            <span>Remboursé</span>
            <span>{formatPercent(repaid)} %</span>
          </div>
          <div className="h-2 bg-white/5 rounded-full overflow-hidden">
            <div
              className="h-full bg-emerald-400/80 transition-all duration-500"
              style={{ width: `${Math.min(100, Math.max(0, repaid))}%` }}
            />
          </div>
        </div>
      )}

      <dl className="grid grid-cols-2 md:grid-cols-4 gap-3 mt-4 text-sm">
        <div>
          <dt className="text-white/40 text-xs">Mensualité</dt>
          <dd className="text-white/90">{formatCurrency(loan.monthlyPayment)}</dd>
        </div>
        <div>
          <dt className="text-white/40 text-xs">Taux</dt>
          <dd className="text-white/90">{formatPercent(loan.annualRatePercent, 3)} %</dd>
        </div>
        <div>
          <dt className="text-white/40 text-xs">Il reste</dt>
          <dd className="text-white/90">{formatDuration(loan.remainingInstallments)}</dd>
        </div>
        <div>
          <dt className="text-white/40 text-xs">Dernière échéance</dt>
          <dd className="text-white/90">{formatDate(loan.finalDueDate)}</dd>
        </div>
        <div>
          <dt className="text-white/40 text-xs">Prochaine</dt>
          <dd className="text-white/90">{formatDate(loan.nextDueDate)}</dd>
        </div>
        <div>
          <dt className="text-white/40 text-xs">Intérêts restants</dt>
          <dd className="text-amber-300/90">{formatCurrency(loan.remainingInterest)}</dd>
        </div>
        <div>
          <dt className="text-white/40 text-xs">Total à décaisser</dt>
          <dd className="text-white/90">{formatCurrency(loan.remainingPayments)}</dd>
        </div>
        <div>
          <dt className="text-white/40 text-xs">Capital emprunté</dt>
          <dd className="text-white/90">
            {loan.initialPrincipal != null ? formatCurrency(loan.initialPrincipal) : '—'}
          </dd>
        </div>
      </dl>

      <div className="flex flex-wrap items-center gap-3 mt-4 text-xs">
        <button
          onClick={() => setShowSchedule((v) => !v)}
          className="text-white/60 hover:text-white transition-colors"
        >
          {showSchedule ? 'Masquer' : 'Voir'} les 12 prochaines échéances
        </button>
        <span className="text-white/20">·</span>
        <button onClick={() => onEdit(loan)} className="text-white/60 hover:text-white transition-colors">
          Modifier
        </button>
        <span className="text-white/20">·</span>
        <button onClick={() => onDelete(loan)} className="text-rose-300/70 hover:text-rose-300 transition-colors">
          Supprimer
        </button>
      </div>

      {showSchedule && (
        <div className="mt-4 overflow-x-auto">
          {isLoading && <p className="text-white/40 text-sm">Chargement…</p>}
          {schedule && schedule.length === 0 && <p className="text-white/40 text-sm">Emprunt éteint.</p>}
          {schedule && schedule.length > 0 && (
            <table className="w-full text-sm min-w-[520px]">
              <thead>
                <tr className="text-white/40 text-xs uppercase tracking-wider">
                  <th className="text-left font-medium py-2">Échéance</th>
                  <th className="text-right font-medium py-2">À payer</th>
                  <th className="text-right font-medium py-2">Capital</th>
                  <th className="text-right font-medium py-2">Intérêts</th>
                  <th className="text-right font-medium py-2">Solde après</th>
                </tr>
              </thead>
              <tbody>
                {schedule.map((i) => (
                  <tr key={i.dueDate} className="border-t border-white/5">
                    <td className="py-1.5 text-white/70">{formatDate(i.dueDate)}</td>
                    <td className="py-1.5 text-right text-white/90">{formatCurrency(i.payment)}</td>
                    <td className="py-1.5 text-right text-emerald-300/80">{formatCurrency(i.principal)}</td>
                    <td className="py-1.5 text-right text-amber-300/80">{formatCurrency(i.interest)}</td>
                    <td className="py-1.5 text-right text-white/60">{formatCurrency(i.remainingPrincipal)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
    </div>
  );
};

const Debts = () => {
  const { currentDashboard } = useDashboards();
  const dashboardId = currentDashboard?.id;
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const { data: loans, isLoading } = useLoansQuery(dashboardId);
  const { data: summary } = useDebtSummaryQuery(dashboardId);
  const { data: balances } = useAccountBalancesQuery(dashboardId);
  const { data: investments } = useInvestmentsQuery(dashboardId);

  const [form, setForm] = useState<LoanForm>(emptyForm);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [saving, setSaving] = useState(false);

  const netWorth = useMemo(() => {
    const cash = balances?.reduce((s, b) => s + b.balance, 0) ?? 0;
    const invested = investments
      ?.filter((i) => !i.isArchived && i.marketValue != null)
      .reduce((s, i) => s + (i.marketValue ?? 0), 0) ?? 0;
    const debt = summary?.totalRemainingPrincipal ?? 0;
    return { cash, invested, debt, net: cash + invested - debt };
  }, [balances, investments, summary]);

  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['loans', dashboardId] });
    queryClient.invalidateQueries({ queryKey: ['debt-summary', dashboardId] });
    queryClient.invalidateQueries({ queryKey: ['loan-schedule'] });
  };

  const openCreate = () => {
    setForm(emptyForm);
    setEditingId(null);
    setShowForm(true);
  };

  const openEdit = (loan: Loan) => {
    setForm(toForm(loan));
    setEditingId(loan.id);
    setShowForm(true);
  };

  const submit = async () => {
    if (!dashboardId) return;

    // Un montant copié d'un relevé arrive en « 1 232,72 », espace insécable compris.
    // Sans le garde sur la finitude, le NaN filait jusqu'au backend et remontait
    // en 400 générique, qui accusait la mensualité à tort.
    const num = (v: string): number | null | undefined => {
      const cleaned = v.replace(/\s/g, '').replace(',', '.');
      if (cleaned === '') return null;
      const parsed = Number(cleaned);
      return Number.isFinite(parsed) ? parsed : undefined;
    };

    const rate = num(form.annualRatePercent);
    const payment = num(form.monthlyPayment);
    const principal = num(form.anchorPrincipal);
    const initial = num(form.initialPrincipal);

    if ([rate, payment, principal, initial].includes(undefined)) {
      showToast('Le taux et les montants doivent être des nombres.', 'error');
      return;
    }

    if (!form.name.trim() || rate == null || payment == null || principal == null || !form.anchorDate) {
      showToast('Nom, taux, mensualité, date et solde d’ancrage sont obligatoires.', 'error');
      return;
    }

    const payload: CreateLoan = {
      dashboardId,
      name: form.name.trim(),
      holder: form.holder.trim(),
      kind: Number(form.kind),
      // Chaîne vide et 0 valent « inconnu » côté API : c'est ce qui permet d'effacer un champ.
      lender: form.lender.trim(),
      reference: form.reference.trim(),
      initialPrincipal: initial ?? 0,
      annualRatePercent: rate,
      monthlyPayment: payment,
      anchorDate: form.anchorDate,
      anchorPrincipal: principal,
      debitIban: form.debitIban.trim(),
    };

    setSaving(true);
    try {
      if (editingId) {
        const { dashboardId: _drop, ...rest } = payload;
        void _drop;
        await loansApi.update(editingId, rest);
        showToast('Emprunt mis à jour.', 'success');
      } else {
        await loansApi.create(payload);
        showToast('Emprunt ajouté.', 'success');
      }
      setShowForm(false);
      setEditingId(null);
      setForm(emptyForm);
      refresh();
    } catch {
      showToast("L'enregistrement a échoué. Vérifie que la mensualité couvre les intérêts.", 'error');
    } finally {
      setSaving(false);
    }
  };

  const remove = async (loan: Loan) => {
    if (!window.confirm(`Supprimer « ${loan.name} » ? L'historique du prêt sera perdu.`)) return;
    try {
      await loansApi.delete(loan.id);
      showToast('Emprunt supprimé.', 'success');
      refresh();
    } catch {
      showToast('La suppression a échoué.', 'error');
    }
  };

  return (
    <div className="space-y-5 md:space-y-6 animate-[fadeIn_0.15s_ease-out]">
      <div className="flex flex-col md:flex-row md:items-end md:justify-between gap-3">
        <div>
          <h2
            className="text-2xl md:text-3xl font-bold text-white"
            style={{ fontFamily: "'Space Grotesk', sans-serif" }}
          >
            Dettes
          </h2>
          <p className="text-white/40 text-xs md:text-sm mt-1">
            Le passif ne remonte pas des banques. Chaque emprunt est ancré sur une ligne de son tableau
            d’amortissement, le reste se recalcule.
          </p>
        </div>
        <button
          onClick={openCreate}
          className="self-start md:self-auto px-4 py-2 rounded-lg bg-white/10 hover:bg-white/20 border border-white/10 text-sm text-white transition-colors"
        >
          Ajouter un emprunt
        </button>
      </div>

      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 md:gap-4">
        <Kpi
          label="Capital restant dû"
          value={formatCurrency(summary?.totalRemainingPrincipal ?? 0)}
          hint={`${summary?.loanCount ?? 0} emprunt${(summary?.loanCount ?? 0) > 1 ? 's' : ''}`}
          tone="rose"
        />
        <Kpi
          label="Charge mensuelle"
          value={formatCurrency(summary?.totalMonthlyPayment ?? 0)}
          hint="prochaines échéances"
        />
        <Kpi
          label="Intérêts restants"
          value={formatCurrency(summary?.totalRemainingInterest ?? 0)}
          hint="jusqu’à extinction"
          tone="amber"
        />
        <Kpi
          label="Libéré en"
          value={formatMonthYear(summary?.debtFreeDate)}
          hint="dernière échéance"
          tone="emerald"
        />
      </div>

      <div className={`${cardClass} p-5`}>
        <h3 className="text-xs font-semibold uppercase tracking-wider text-white/50 mb-4">Patrimoine net</h3>
        <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm text-white/60">
          <span className="text-white/90">{formatCurrency(netWorth.cash)}</span>
          <span className="text-white/30">comptes</span>
          <span className="text-white/30">+</span>
          <span className="text-white/90">{formatCurrency(netWorth.invested)}</span>
          <span className="text-white/30">investis</span>
          <span className="text-white/30">−</span>
          <span className="text-rose-300">{formatCurrency(netWorth.debt)}</span>
          <span className="text-white/30">de dettes</span>
        </div>
        <p
          className={`text-3xl font-bold mt-3 ${netWorth.net >= 0 ? 'text-emerald-300' : 'text-rose-300'}`}
          style={{ fontFamily: "'Space Grotesk', sans-serif" }}
        >
          {formatCurrency(netWorth.net)}
        </p>
        <p className="text-white/40 text-xs mt-1">
          Hors valeur des biens immobiliers, qui ne sont pas suivis dans l’application.
        </p>
      </div>

      {showForm && (
        <div className={`${cardClass} p-5`}>
          <h3 className="text-white font-semibold mb-4">
            {editingId ? 'Modifier l’emprunt' : 'Nouvel emprunt'}
          </h3>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            <div className="md:col-span-2">
              <label className={labelClass}>Nom</label>
              <input
                className={inputClass}
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                placeholder="Crédit logement"
              />
            </div>
            <div>
              <label className={labelClass}>Type</label>
              <select
                className={inputClass}
                value={form.kind}
                onChange={(e) => setForm({ ...form, kind: Number(e.target.value) })}
              >
                {Object.entries(kindLabels).map(([value, label]) => (
                  <option key={value} value={value} className="bg-slate-800">
                    {label}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className={labelClass}>Prêteur</label>
              <input
                className={inputClass}
                value={form.lender}
                onChange={(e) => setForm({ ...form, lender: e.target.value })}
                placeholder="CBC Banque"
              />
            </div>
            <div>
              <label className={labelClass}>Titulaire</label>
              <input
                className={inputClass}
                value={form.holder}
                onChange={(e) => setForm({ ...form, holder: e.target.value })}
                placeholder="Commun"
              />
            </div>
            <div>
              <label className={labelClass}>Référence du dossier</label>
              <input
                className={inputClass}
                value={form.reference}
                onChange={(e) => setForm({ ...form, reference: e.target.value })}
              />
            </div>
            <div>
              <label className={labelClass}>Capital emprunté</label>
              <input
                className={inputClass}
                value={form.initialPrincipal}
                onChange={(e) => setForm({ ...form, initialPrincipal: e.target.value })}
                placeholder="265000"
              />
            </div>
            <div>
              <label className={labelClass}>Taux annuel (%)</label>
              <input
                className={inputClass}
                value={form.annualRatePercent}
                onChange={(e) => setForm({ ...form, annualRatePercent: e.target.value })}
                placeholder="1.2828"
              />
            </div>
            <div>
              <label className={labelClass}>Mensualité</label>
              <input
                className={inputClass}
                value={form.monthlyPayment}
                onChange={(e) => setForm({ ...form, monthlyPayment: e.target.value })}
                placeholder="1232.72"
              />
            </div>
            <div>
              <label className={labelClass}>Date de l’échéance de référence</label>
              <input
                type="date"
                className={inputClass}
                value={form.anchorDate}
                onChange={(e) => setForm({ ...form, anchorDate: e.target.value })}
              />
            </div>
            <div>
              <label className={labelClass}>Solde après cette échéance</label>
              <input
                className={inputClass}
                value={form.anchorPrincipal}
                onChange={(e) => setForm({ ...form, anchorPrincipal: e.target.value })}
                placeholder="172856.22"
              />
            </div>
            <div>
              <label className={labelClass}>IBAN débité</label>
              <input
                className={inputClass}
                value={form.debitIban}
                onChange={(e) => setForm({ ...form, debitIban: e.target.value })}
              />
            </div>
          </div>

          <p className="text-white/40 text-xs mt-3">
            Si tu ne connais que la date de fin, mets-la en échéance de référence avec un solde de 0. Le calcul
            remonte le temps tout seul.
          </p>

          <div className="flex gap-3 mt-4">
            <button
              onClick={submit}
              disabled={saving}
              className="px-4 py-2 rounded-lg bg-emerald-500/20 hover:bg-emerald-500/30 border border-emerald-400/30 text-sm text-emerald-200 transition-colors disabled:opacity-50"
            >
              {saving ? 'Enregistrement…' : 'Enregistrer'}
            </button>
            <button
              onClick={() => {
                setShowForm(false);
                setEditingId(null);
              }}
              className="px-4 py-2 rounded-lg bg-white/5 hover:bg-white/10 border border-white/10 text-sm text-white/70 transition-colors"
            >
              Annuler
            </button>
          </div>
        </div>
      )}

      {isLoading && <p className="text-white/40">Chargement…</p>}

      {!isLoading && loans && loans.length === 0 && (
        <div className={`${cardClass} p-8 text-center`}>
          <p className="text-white/50">Aucun emprunt enregistré.</p>
        </div>
      )}

      <div className="space-y-4">
        {loans?.map((loan) => (
          <LoanCard key={loan.id} loan={loan} onEdit={openEdit} onDelete={remove} />
        ))}
      </div>
    </div>
  );
};

export default Debts;
