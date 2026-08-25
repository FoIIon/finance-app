import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../hooks/usePeriod';
import { useSummaryQuery } from '../../hooks/queries';
import { formatCurrency } from '../../utils/format';

export const ExceptionalToggle = () => {
  const { currentDashboard } = useDashboards();
  const { period, includeExceptional, setIncludeExceptional } = usePeriod();
  const { data: summary } = useSummaryQuery(currentDashboard?.id, period);

  const exceptional = Number(summary?.exceptionalExpenses ?? 0);
  const hasExceptional = exceptional > 0;

  return (
    <div className="flex items-center gap-2 flex-wrap justify-end">
      <button
        onClick={() => setIncludeExceptional(!includeExceptional)}
        className={`px-3 py-1.5 rounded-xl text-sm font-medium border transition-all flex items-center gap-2 ${
          includeExceptional
            ? 'bg-amber-500/20 text-amber-300 border-amber-500/30'
            : 'bg-white/5 text-white/60 border-white/10 hover:text-white hover:bg-white/5'
        }`}
        title="Inclure les dépenses exceptionnelles dans les totaux"
      >
        <span aria-hidden="true">⚡</span>
        <span>{includeExceptional ? 'Exceptionnel inclus' : 'Hors exceptionnel'}</span>
      </button>
      {!includeExceptional && hasExceptional && (
        <span className="text-white/40 text-xs">
          dont {formatCurrency(exceptional)} exceptionnels exclus
        </span>
      )}
    </div>
  );
};
