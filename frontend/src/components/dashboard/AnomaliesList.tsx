import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../context/PeriodContext';
import { useAnomaliesQuery } from '../../hooks/queries';
import { formatCurrency } from '../../utils/format';

export const AnomaliesList = () => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const { data, isLoading } = useAnomaliesQuery(currentDashboard?.id, period);

  return (
    <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
      <h3 className="text-base md:text-lg font-semibold text-white mb-4">Dépenses inhabituelles</h3>
      {isLoading ? (
        <div className="space-y-2">
          {[1, 2].map(i => <div key={i} className="h-14 rounded bg-white/5 animate-pulse" />)}
        </div>
      ) : !data || data.length === 0 ? (
        <p className="text-white/30 text-center py-6 text-sm">
          Rien d'inhabituel sur cette période ✓
        </p>
      ) : (
        <div className="space-y-3">
          {data.map((t) => (
            <div key={t.id} className="flex items-center justify-between gap-3 p-3 bg-amber-500/5 rounded-xl border border-amber-500/20">
              <div className="flex items-center gap-3 min-w-0">
                <span className="text-xl flex-shrink-0" aria-hidden="true">{t.categoryIcon}</span>
                <div className="min-w-0">
                  <p className="text-white text-sm font-medium truncate">{t.description || <em className="text-white/30">(sans libellé)</em>}</p>
                  <p className="text-white/40 text-xs truncate">
                    {t.categoryName} · {new Date(t.date).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short' })}
                  </p>
                </div>
              </div>
              <span className="text-sm font-bold text-amber-300 whitespace-nowrap">
                {formatCurrency(t.amount)}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
