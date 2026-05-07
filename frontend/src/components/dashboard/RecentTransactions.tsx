import { Link } from 'react-router-dom';
import { useDashboards } from '../../hooks/useDashboards';
import { useRecentTransactionsQuery } from '../../hooks/queries';
import { TransactionType } from '../../types/transaction';
import { formatCurrency } from '../../utils/format';

export const RecentTransactions = ({ limit = 5 }: { limit?: number }) => {
  const { currentDashboard } = useDashboards();
  const { data, isLoading } = useRecentTransactionsQuery(currentDashboard?.id, limit);

  return (
    <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-base md:text-lg font-semibold text-white">Dernières transactions</h3>
        <Link to="/transactions" className="text-xs text-amber-400 hover:text-amber-300 transition-colors">
          Voir tout →
        </Link>
      </div>
      {isLoading ? (
        <div className="space-y-2">
          {[1, 2, 3].map(i => <div key={i} className="h-12 rounded bg-white/5 animate-pulse" />)}
        </div>
      ) : !data || data.length === 0 ? (
        <p className="text-white/30 text-center py-6 text-sm">Aucune transaction</p>
      ) : (
        <div className="space-y-2">
          {data.map((t) => (
            <div key={t.id} className="flex items-center justify-between py-2 border-b border-white/5 last:border-0">
              <div className="flex items-center gap-3 min-w-0">
                <span className="text-xl flex-shrink-0" aria-hidden="true">{t.categoryIcon}</span>
                <div className="min-w-0 flex-1">
                  <p className="text-white text-sm font-medium truncate">{t.description || <em className="text-white/30">(sans libellé)</em>}</p>
                  <p className="text-white/40 text-xs truncate">
                    {t.categoryName}
                    {t.counterpartyName && <> · {t.counterpartyName}</>}
                    <> · {new Date(t.date).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short' })}</>
                  </p>
                </div>
              </div>
              <span className={`text-sm font-semibold whitespace-nowrap ml-2 ${t.type === TransactionType.Income ? 'text-emerald-400' : 'text-red-400'}`}>
                {t.type === TransactionType.Income ? '+' : '-'}{formatCurrency(t.amount)}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
