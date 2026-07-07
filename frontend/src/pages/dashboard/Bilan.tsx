import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../context/PeriodContext';
import { transactionsApi } from '../../api/transactions';
import { formatCurrency } from '../../utils/format';
import { makeMonthPeriod } from '../../utils/periods';
import type { CategoryBreakdown } from '../../types/transaction';
import { CategoryDetailModal } from '../../components/dashboard/CategoryDetailModal';

const pad2 = (n: number) => String(n).padStart(2, '0');

/**
 * Résout le mois à afficher à partir de la période globale.
 * Mois précis (this-month / last-month / month-YYYY-MM) → ce mois-là.
 * Périodes multi-mois (3 mois, année, tout) → mois en cours + mention discrète.
 */
const resolveMonth = (periodKey: string): { year: number; month: number; isSpecific: boolean } => {
  const now = new Date();
  if (periodKey === 'this-month') {
    return { year: now.getFullYear(), month: now.getMonth() + 1, isSpecific: true };
  }
  if (periodKey === 'last-month') {
    const d = new Date(now.getFullYear(), now.getMonth() - 1, 1);
    return { year: d.getFullYear(), month: d.getMonth() + 1, isSpecific: true };
  }
  if (periodKey.startsWith('month-')) {
    const [y, m] = periodKey.slice('month-'.length).split('-');
    return { year: parseInt(y, 10), month: parseInt(m, 10), isSpecific: true };
  }
  return { year: now.getFullYear(), month: now.getMonth() + 1, isSpecific: false };
};

interface BlockAccent {
  amount: string;
  bar: string;
}

const ACCENTS: Record<string, BlockAccent> = {
  entrees: { amount: 'text-emerald-300', bar: '#34d399' },
  fixe: { amount: 'text-blue-300', bar: '#60a5fa' },
  mises: { amount: 'text-violet-300', bar: '#a78bfa' },
  variable: { amount: 'text-amber-300', bar: '#fbbf24' },
};

interface BlockCardProps {
  title: string;
  amount: number;
  accent: BlockAccent;
  categories: CategoryBreakdown[];
  onCategoryClick?: (c: CategoryBreakdown) => void;
  footNote?: string;
  emptyLabel: string;
}

const BlockCard = ({ title, amount, accent, categories, onCategoryClick, footNote, emptyLabel }: BlockCardProps) => {
  const [expanded, setExpanded] = useState(false);
  const max = categories[0]?.amount ?? 0;
  const visible = expanded ? categories : categories.slice(0, 5);
  const hidden = categories.length - visible.length;

  return (
    <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
      <div className="flex items-baseline justify-between gap-3 mb-4">
        <h3 className="text-xs font-semibold uppercase tracking-wider text-white/50">{title}</h3>
        <span className={`text-2xl font-bold ${accent.amount}`} style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
          {formatCurrency(amount)}
        </span>
      </div>

      {footNote && <p className="text-white/40 text-xs -mt-2 mb-3">{footNote}</p>}

      {categories.length === 0 ? (
        <p className="text-white/30 text-sm py-2">{emptyLabel}</p>
      ) : (
        <div className="space-y-1.5">
          {visible.map((c) => {
            const pct = max > 0 ? (Number(c.amount) / max) * 100 : 0;
            const Row = (
              <>
                <div className="flex items-center justify-between mb-1 text-sm">
                  <span className="flex items-center gap-2 min-w-0 flex-1">
                    <span aria-hidden="true">{c.categoryIcon}</span>
                    <span className="text-white/80 truncate">{c.categoryName}</span>
                  </span>
                  <span className="text-white/60 text-xs whitespace-nowrap ml-2">{formatCurrency(c.amount)}</span>
                </div>
                <div className="h-1.5 bg-white/5 rounded-full overflow-hidden">
                  <div className="h-full transition-all duration-500" style={{ width: `${pct}%`, backgroundColor: c.categoryColor, opacity: 0.85 }} />
                </div>
              </>
            );
            return onCategoryClick ? (
              <button
                key={c.categoryId}
                onClick={() => onCategoryClick(c)}
                className="w-full text-left rounded-lg p-1 -m-1 hover:bg-white/5 transition-colors cursor-pointer"
                title="Voir les transactions"
              >
                {Row}
              </button>
            ) : (
              <div key={c.categoryId} className="p-1 -m-1">{Row}</div>
            );
          })}
          {categories.length > 5 && (
            <button
              onClick={() => setExpanded((v) => !v)}
              className="text-xs text-white/50 hover:text-white/80 transition-colors mt-1"
            >
              {expanded ? 'Réduire' : `Tout voir (${hidden} de plus)`}
            </button>
          )}
        </div>
      )}
    </div>
  );
};

const DashboardBilan = () => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const [selected, setSelected] = useState<CategoryBreakdown | null>(null);

  const { year, month, isSpecific } = useMemo(() => resolveMonth(period.key), [period.key]);
  const monthPeriod = useMemo(() => makeMonthPeriod(`${year}-${pad2(month)}`), [year, month]);

  const { data: report, isLoading } = useQuery({
    queryKey: ['monthly-report', currentDashboard?.id, year, month],
    enabled: !!currentDashboard?.id,
    queryFn: async () => {
      const res = await transactionsApi.getMonthlyReport(currentDashboard!.id, year, month);
      return res.data;
    },
  });

  if (isLoading || !report) {
    return (
      <div className="space-y-5">
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
          {[1, 2, 3, 4].map((i) => <div key={i} className="h-56 rounded-2xl bg-white/5 animate-pulse" />)}
        </div>
        <div className="h-32 rounded-2xl bg-white/5 animate-pulse" />
      </div>
    );
  }

  const totalPositive = report.total >= 0;
  const noFixed = report.fixeByCategory.length === 0;

  return (
    <div className="space-y-5 animate-[fadeIn_0.15s_ease-out]">
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-lg font-semibold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
          Bilan · {monthPeriod.label}
        </h3>
      </div>

      {!isSpecific && (
        <p className="text-white/40 text-xs -mt-2">
          La période « {period.label} » couvre plusieurs mois. Le bilan affiche le mois en cours ({monthPeriod.label}).
        </p>
      )}

      {noFixed && (
        <div className="bg-gradient-to-br from-blue-500/5 to-violet-500/5 backdrop-blur-xl rounded-2xl border border-blue-500/20 p-5">
          <p className="text-white/80 text-sm">
            Coche « charge fixe » sur tes règles de catégorisation (prêt, électricité, crèche…) dans{' '}
            <Link to="/bank" className="text-blue-300 font-medium hover:text-blue-200 underline underline-offset-2">
              Comptes bancaires
            </Link>{' '}
            pour un bilan fidèle. Sans ça, tout passe en variable. Le 📌 sur une transaction permet d'ajuster au cas par cas.
          </p>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
        <BlockCard
          title="Entrées"
          amount={report.entrees}
          accent={ACCENTS.entrees}
          categories={report.entreesByCategory}
          emptyLabel="Aucune entrée ce mois-ci"
        />
        <BlockCard
          title="Charges fixes"
          amount={report.fixe}
          accent={ACCENTS.fixe}
          categories={report.fixeByCategory}
          onCategoryClick={setSelected}
          emptyLabel="Aucune transaction marquée fixe"
        />
        <BlockCard
          title="Mises de côté"
          amount={report.misesDeCote}
          accent={ACCENTS.mises}
          categories={report.misesDeCoteByCategory}
          onCategoryClick={setSelected}
          emptyLabel="Aucune mise de côté ce mois-ci"
        />
        <BlockCard
          title="Variable"
          amount={report.variable}
          accent={ACCENTS.variable}
          categories={report.variableByCategory}
          onCategoryClick={setSelected}
          footNote={report.variableExceptionnel > 0 ? `dont ${formatCurrency(report.variableExceptionnel)} exceptionnels` : undefined}
          emptyLabel="Aucune dépense variable ce mois-ci"
        />
      </div>

      {/* TOTAL DU MOIS */}
      <div className={`rounded-2xl border p-6 backdrop-blur-xl ${totalPositive ? 'bg-emerald-500/5 border-emerald-500/25' : 'bg-red-500/5 border-red-500/25'}`}>
        <div className="flex items-center justify-between gap-3">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wider text-white/50">Total du mois</p>
            <p className="text-white/40 text-xs mt-1">Entrées − fixe − mises de côté − variable</p>
          </div>
          <span
            className={`text-3xl md:text-4xl font-bold ${totalPositive ? 'text-emerald-300' : 'text-red-400'}`}
            style={{ fontFamily: "'Space Grotesk', sans-serif" }}
          >
            {formatCurrency(report.total)}
          </span>
        </div>
      </div>

      {selected && currentDashboard && (
        <CategoryDetailModal
          categoryId={selected.categoryId}
          categoryName={selected.categoryName}
          categoryIcon={selected.categoryIcon}
          categoryColor={selected.categoryColor}
          totalAmount={Number(selected.amount)}
          dashboardId={currentDashboard.id}
          period={monthPeriod}
          onClose={() => setSelected(null)}
        />
      )}
    </div>
  );
};

export default DashboardBilan;
