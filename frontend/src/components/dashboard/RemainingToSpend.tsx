import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../context/PeriodContext';
import { useSummaryQuery } from '../../hooks/queries';
import { formatCurrency } from '../../utils/format';

/**
 * Reste à vivre = revenus − dépenses (sur la période sélectionnée).
 * Si la période = ce mois-ci, on projette sur la fin du mois pro-rata.
 */
export const RemainingToSpend = () => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const { data: summary, isLoading } = useSummaryQuery(currentDashboard?.id, period);

  const income = Number(summary?.totalIncome ?? 0);
  const spent = Number(summary?.totalExpenses ?? 0);
  const remaining = income - spent;

  // Pro-rata si on est sur "ce mois-ci"
  let projectionLine: string | null = null;
  if (period.key === 'this-month') {
    const now = new Date();
    const dayOfMonth = now.getDate();
    const lastDay = new Date(now.getFullYear(), now.getMonth() + 1, 0).getDate();
    const dailyAvg = spent / Math.max(1, dayOfMonth);
    const projected = dailyAvg * lastDay;
    projectionLine = `Projection fin de mois : ${formatCurrency(projected)} de dépenses`;
  }

  return (
    <div className="bg-gradient-to-br from-amber-500/10 to-orange-600/10 backdrop-blur-xl rounded-2xl border border-amber-500/30 p-5 md:p-6">
      <p className="text-amber-300/60 text-xs md:text-sm uppercase tracking-wider mb-1">Reste à vivre</p>
      {isLoading ? (
        <div className="h-12 w-32 rounded bg-white/5 animate-pulse" />
      ) : (
        <p className={`text-3xl md:text-4xl font-bold ${remaining >= 0 ? 'text-emerald-300' : 'text-red-300'}`}>
          {remaining >= 0 ? '+' : ''}{formatCurrency(remaining)}
        </p>
      )}
      <p className="text-white/50 text-xs mt-2">
        {formatCurrency(income)} revenus − {formatCurrency(spent)} dépenses
      </p>
      {projectionLine && (
        <p className="text-amber-200/40 text-xs mt-1 italic">{projectionLine}</p>
      )}
    </div>
  );
};
