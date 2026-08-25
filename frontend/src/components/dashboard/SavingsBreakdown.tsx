import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../hooks/usePeriod';
import { useSummaryQuery } from '../../hooks/queries';
import { formatCurrency } from '../../utils/format';

/**
 * Montre la mise de côté (transferts vers épargne, comptes joints, etc.) sur la période.
 * Ces montants sont exclus du Bilan / dépenses pour ne pas être double-comptés, mais ils sortent
 * bien du compte courant — d'où l'importance de les rendre visibles ici.
 */
export const SavingsBreakdownWidget = () => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const { data, isLoading } = useSummaryQuery(currentDashboard?.id, period);

  const total = Number(data?.totalSavings ?? 0);
  const items = data?.savingsBreakdown ?? [];

  return (
    <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
      <div className="flex items-baseline justify-between mb-3">
        <h3 className="text-base md:text-lg font-semibold text-white">Mise de côté</h3>
        <span className="text-white/40 text-xs">{period.label.toLowerCase()}</span>
      </div>
      {isLoading ? (
        <div className="h-12 rounded bg-white/5 animate-pulse" />
      ) : total === 0 ? (
        <p className="text-white/30 text-center text-sm py-4">Aucun transfert vers épargne sur la période.</p>
      ) : (
        <div className="space-y-3">
          <p className="text-2xl md:text-3xl font-bold text-emerald-400">
            {formatCurrency(total)}
          </p>
          <div className="space-y-2 pt-2 border-t border-white/5">
            {items.map((c) => (
              <div key={c.categoryName} className="flex items-center justify-between gap-2">
                <span className="flex items-center gap-2 text-sm text-white/80 min-w-0">
                  <span aria-hidden="true">{c.categoryIcon}</span>
                  <span className="truncate">{c.categoryName}</span>
                </span>
                <span className="text-white/90 text-sm font-medium whitespace-nowrap">
                  {formatCurrency(c.amount)}
                  <span className="text-white/30 text-xs ml-1.5">{c.percentage.toFixed(0)}%</span>
                </span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};
