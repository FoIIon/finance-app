import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../context/PeriodContext';
import { useBudgetsProgressQuery } from '../../hooks/queries';
import { formatCurrency } from '../../utils/format';
import { CategoryDetailModal } from './CategoryDetailModal';

export const BudgetProgressWidget = () => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const { data, isLoading } = useBudgetsProgressQuery(currentDashboard?.id, period);
  const [selectedBudgetId, setSelectedBudgetId] = useState<number | null>(null);
  const selected = data?.find((b) => b.budgetId === selectedBudgetId) ?? null;

  return (
    <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-base md:text-lg font-semibold text-white">Budgets — {period.label}</h3>
        <Link to="/budgets" className="text-xs text-amber-400 hover:text-amber-300 transition-colors">
          Gérer →
        </Link>
      </div>

      {isLoading ? (
        <div className="space-y-3">
          {[1, 2, 3].map(i => <div key={i} className="h-12 rounded bg-white/5 animate-pulse" />)}
        </div>
      ) : !data || data.length === 0 ? (
        <div className="py-6 text-center text-white/30 text-sm">
          Aucun budget défini.
          <Link to="/budgets" className="block mt-2 text-amber-400 text-xs hover:underline">
            + Créer un budget
          </Link>
        </div>
      ) : (
        <div className="space-y-3">
          {data.map((b) => {
            const pct = Math.min(100, b.progressPct);
            const barColor =
              b.status === 'exceeded' ? 'bg-red-500' :
              b.status === 'warning' ? 'bg-amber-500' :
              'bg-emerald-500';
            return (
              <button
                key={b.budgetId}
                onClick={() => setSelectedBudgetId(b.budgetId)}
                className="w-full text-left rounded-lg p-1 -m-1 hover:bg-white/5 transition-colors cursor-pointer"
                title="Voir l'évolution de la catégorie"
              >
                <div className="flex items-center justify-between mb-1">
                  <div className="flex items-center gap-2 min-w-0">
                    <span aria-hidden="true">{b.categoryIcon}</span>
                    <span className="text-white/80 text-sm font-medium truncate">{b.categoryName}</span>
                  </div>
                  <div className="text-right text-xs whitespace-nowrap">
                    <span className={
                      b.status === 'exceeded' ? 'text-red-400 font-semibold' :
                      b.status === 'warning' ? 'text-amber-400' :
                      'text-white/60'
                    }>
                      {formatCurrency(b.spent)}
                    </span>
                    <span className="text-white/40"> / {formatCurrency(b.budget)}</span>
                  </div>
                </div>
                <div className="relative h-2 bg-white/5 rounded-full overflow-hidden">
                  <div
                    className={`absolute left-0 top-0 h-full ${barColor} transition-all duration-500`}
                    style={{ width: `${pct}%` }}
                  />
                </div>
                <div className="mt-1 text-xs text-white/40 flex justify-between">
                  <span>{b.progressPct.toFixed(0)}%</span>
                  <span>{b.remaining < 0 ? 'Dépassement ' : 'Reste '}{formatCurrency(Math.abs(b.remaining))}</span>
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
          categoryColor={selected.categoryColor || '#f59e0b'}
          totalAmount={Number(selected.spent)}
          dashboardId={currentDashboard.id}
          period={period}
          onClose={() => setSelectedBudgetId(null)}
        />
      )}
    </div>
  );
};
