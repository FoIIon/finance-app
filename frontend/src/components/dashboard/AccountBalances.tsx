import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useDashboards } from '../../hooks/useDashboards';
import { useAccountBalancesQuery } from '../../hooks/queries';
import { dashboardExtrasApi } from '../../api/savingsGoals';
import { formatCurrency } from '../../utils/format';
import { usePeriod } from '../../hooks/usePeriod';
import { periodToRange } from '../../utils/periods';

export const AccountBalancesWidget = () => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const { data, isLoading } = useAccountBalancesQuery(currentDashboard?.id, period);
  const [refreshing, setRefreshing] = useState(false);
  const qc = useQueryClient();
  const { from } = periodToRange(period);

  const handleRefresh = async () => {
    if (!currentDashboard) return;
    setRefreshing(true);
    try {
      await dashboardExtrasApi.refreshBalances(currentDashboard.id);
      qc.invalidateQueries({ queryKey: ['account-balances'] });
    } finally {
      setRefreshing(false);
    }
  };

  const totalBalance = data?.reduce((s, b) => s + Number(b.balance), 0) ?? 0;
  const accountCount = data?.length ?? 0;

  return (
    <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h3 className="text-base md:text-lg font-semibold text-white">Soldes par compte</h3>
          {accountCount > 1 && (
            <p className="text-white/40 text-xs">
              {accountCount} comptes · total <span className={`font-semibold ${totalBalance >= 0 ? 'text-emerald-400' : 'text-red-400'}`}>{formatCurrency(totalBalance)}</span>
            </p>
          )}
        </div>
        <button
          onClick={handleRefresh}
          disabled={refreshing}
          className="text-xs px-2 py-1 rounded-lg bg-white/5 hover:bg-white/10 text-white/60 hover:text-white transition-colors disabled:opacity-50 whitespace-nowrap"
          title="Rafraîchir le solde via la banque"
        >
          {refreshing ? '⟳ ...' : '⟳ Actualiser'}
        </button>
      </div>
      {isLoading ? (
        <div className="space-y-2">
          {[1, 2].map(i => <div key={i} className="h-14 rounded bg-white/5 animate-pulse" />)}
        </div>
      ) : !data || data.length === 0 ? (
        <p className="text-white/30 text-center text-sm py-4">Aucun compte lié</p>
      ) : (
        <div className="space-y-3">
          {data.map((b) => {
            const hasDataInPeriod = !from || (b.lastTransactionDate && new Date(b.lastTransactionDate) >= new Date(from));
            return (
              <div key={b.accountId} className={`flex items-center justify-between gap-3 py-2 border-b border-white/5 last:border-0 ${!hasDataInPeriod ? 'opacity-60' : ''}`}>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span aria-hidden="true" className="text-lg flex-shrink-0">🏦</span>
                    <div className="min-w-0">
                      <p className="text-white/90 text-sm font-medium truncate">
                        {b.bankInstitutionName ?? 'Compte'}
                      </p>
                      <p className="text-white/40 text-xs truncate" title={b.accountName}>
                        {b.accountName}
                      </p>
                      {!hasDataInPeriod && (
                        <p className="text-amber-400/70 text-[10px] mt-0.5 italic">
                          aucune transaction sur la période
                        </p>
                      )}
                    </div>
                  </div>
                </div>
                <div className="text-right">
                  <p className={`font-bold whitespace-nowrap ${b.balance >= 0 ? 'text-emerald-400' : 'text-red-400'}`}>
                    {formatCurrency(b.balance)}
                  </p>
                  <p className="text-white/30 text-xs">
                    {b.isRealBalance ? (
                      <span className="text-emerald-400/60">● solde banque</span>
                    ) : (
                      <span className="text-white/30">~ calculé</span>
                    )}
                    {b.balanceUpdatedAt && b.isRealBalance && (
                      <span className="text-white/30">
                        {' · '}
                        {new Date(b.balanceUpdatedAt).toLocaleString('fr-FR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })}
                      </span>
                    )}
                  </p>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
