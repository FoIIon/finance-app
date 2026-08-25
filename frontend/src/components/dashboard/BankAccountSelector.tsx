import { useDashboards } from '../../hooks/useDashboards';
import { useAccountBalancesQuery } from '../../hooks/queries';
import { usePeriod } from '../../hooks/usePeriod';

export const BankAccountSelector = () => {
  const { currentDashboard } = useDashboards();
  const { data: accounts, isLoading } = useAccountBalancesQuery(currentDashboard?.id);
  const { bankAccountFilter, setBankAccountFilter } = usePeriod();

  // Pas de sélecteur si <2 comptes
  if (isLoading || !accounts || accounts.length < 2) return null;

  return (
    <div className="flex items-center gap-2 flex-wrap">
      <span className="text-white/40 text-sm hidden md:inline">Compte :</span>
      <div className="flex gap-1 p-1 rounded-xl bg-white/5 border border-white/10">
        <button
          onClick={() => setBankAccountFilter(undefined)}
          className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-all ${
            bankAccountFilter === undefined
              ? 'bg-amber-500/20 text-amber-300 border border-amber-500/30'
              : 'text-white/60 hover:text-white hover:bg-white/5'
          }`}
        >
          Tous
        </button>
        {accounts.map((a) => (
          <button
            key={a.accountId}
            onClick={() => setBankAccountFilter(a.accountId)}
            className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-all ${
              bankAccountFilter === a.accountId
                ? 'bg-amber-500/20 text-amber-300 border border-amber-500/30'
                : 'text-white/60 hover:text-white hover:bg-white/5'
            }`}
          >
            {a.bankInstitutionName ?? a.accountName}
          </button>
        ))}
      </div>
    </div>
  );
};
