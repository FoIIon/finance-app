import { useDashboards } from '../../hooks/useDashboards';
import { useSavingsGoalsProgressQuery } from '../../hooks/queries';
import { formatCurrency } from '../../utils/format';

export const SavingsGoalsList = () => {
  const { currentDashboard } = useDashboards();
  const { data, isLoading } = useSavingsGoalsProgressQuery(currentDashboard?.id);

  return (
    <div className="space-y-4">
      {isLoading ? (
        <div className="space-y-3">
          {[1, 2].map(i => <div key={i} className="h-32 rounded-2xl bg-white/5 animate-pulse" />)}
        </div>
      ) : !data || data.length === 0 ? (
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-8 text-center">
          <p className="text-white/40 text-sm">Aucun projet d'épargne défini.</p>
          <p className="text-white/30 text-xs mt-1">Vacances, ski, projets maison…</p>
        </div>
      ) : (
        data.map((g) => {
          const pct = Math.min(100, g.progressPct);
          const monthsLabel = g.monthsRemaining === 1 ? 'dans 1 mois' : `dans ${g.monthsRemaining} mois`;
          const targetDateLabel = new Date(g.targetDate).toLocaleDateString('fr-FR', { month: 'long', year: 'numeric' });
          return (
            <div key={g.id} className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5 hover:border-amber-500/30 transition-all">
              <div className="flex items-start justify-between gap-3 mb-3">
                <div className="flex items-center gap-3 min-w-0">
                  <span className="text-2xl flex-shrink-0" aria-hidden="true">{g.icon}</span>
                  <div className="min-w-0">
                    <h4 className="text-white font-semibold truncate">{g.name}</h4>
                    <p className="text-white/40 text-xs">Objectif {targetDateLabel} · {monthsLabel}</p>
                  </div>
                </div>
                <div className="text-right whitespace-nowrap">
                  <p className="text-white font-bold text-base md:text-lg">{formatCurrency(g.currentAmount)}</p>
                  <p className="text-white/40 text-xs">/ {formatCurrency(g.targetAmount)}</p>
                </div>
              </div>
              <div className="relative h-3 bg-white/5 rounded-full overflow-hidden mb-2">
                <div
                  className="h-full bg-gradient-to-r from-amber-500 to-orange-500 transition-all duration-500"
                  style={{ width: `${pct}%` }}
                />
              </div>
              <div className="flex justify-between text-xs">
                <span className="text-white/50">
                  {g.progressPct.toFixed(0)}% atteint
                  {g.isAutoCalculated && <span className="ml-1.5 text-emerald-400" title="Calculé automatiquement">●&nbsp;auto</span>}
                </span>
                <span className="text-amber-300">
                  {g.requiredMonthlySavings > 0 ? `${formatCurrency(g.requiredMonthlySavings)}/mois requis` : 'objectif atteint'}
                </span>
              </div>
            </div>
          );
        })
      )}
    </div>
  );
};
