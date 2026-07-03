import { useState } from 'react';
import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../context/PeriodContext';
import { useSummaryQuery } from '../../hooks/queries';
import { formatCurrency } from '../../utils/format';
import { CategoryDetailModal } from './CategoryDetailModal';

/** Top catégories en barres horizontales (plus lisible que camembert avec 19 cats). */
export const CategoryBreakdownBars = ({ topN = 8 }: { topN?: number }) => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const { data: summary, isLoading } = useSummaryQuery(currentDashboard?.id, period);
  const [selectedIdx, setSelectedIdx] = useState<number | null>(null);

  const categories = (summary?.categoryBreakdown ?? []).slice(0, topN);
  const max = categories[0]?.amount ?? 0;
  const selected = selectedIdx !== null ? categories[selectedIdx] : null;

  return (
    <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
      <h3 className="text-base md:text-lg font-semibold text-white mb-4">
        Top dépenses — {period.label}
      </h3>
      {isLoading ? (
        <div className="space-y-2">
          {[1, 2, 3, 4, 5].map(i => <div key={i} className="h-8 rounded bg-white/5 animate-pulse" />)}
        </div>
      ) : categories.length === 0 ? (
        <p className="text-white/30 text-center py-6 text-sm">Aucune dépense sur cette période</p>
      ) : (
        <div className="space-y-2">
          {categories.map((c, idx) => {
            const pct = max > 0 ? (Number(c.amount) / max) * 100 : 0;
            return (
              <button
                key={c.categoryName}
                onClick={() => setSelectedIdx(idx)}
                className="w-full text-left group rounded-lg p-1 -m-1 hover:bg-white/5 transition-colors cursor-pointer"
                title="Voir les transactions"
              >
                <div className="flex items-center justify-between mb-1 text-sm">
                  <span className="flex items-center gap-2 min-w-0 flex-1">
                    <span aria-hidden="true">{c.categoryIcon}</span>
                    <span className="text-white/80 truncate group-hover:text-white">{c.categoryName}</span>
                  </span>
                  <span className="text-white/50 text-xs whitespace-nowrap ml-2">
                    {formatCurrency(c.amount)} <span className="text-white/30">· {c.percentage}%</span>
                  </span>
                </div>
                <div className="h-2 bg-white/5 rounded-full overflow-hidden">
                  <div
                    className="h-full transition-all duration-500"
                    style={{
                      width: `${pct}%`,
                      backgroundColor: c.categoryColor,
                      opacity: 0.85,
                    }}
                  />
                </div>
              </button>
            );
          })}
        </div>
      )}

      {selected && currentDashboard && (
        <CategoryDetailModal
          categoryId={selected.categoryId}
          categoryName={selected.categoryName}
          categoryIcon={selected.categoryIcon}
          totalAmount={Number(selected.amount)}
          dashboardId={currentDashboard.id}
          period={period}
          onClose={() => setSelectedIdx(null)}
        />
      )}
    </div>
  );
};
