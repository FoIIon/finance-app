import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../context/PeriodContext';
import { useSummaryQuery, useAccountBalancesQuery } from '../../hooks/queries';
import { formatCurrency } from '../../utils/format';

export const KpiCards = () => {
  const { currentDashboard } = useDashboards();
  const { period, bankAccountFilter } = usePeriod();
  const { data: summary, isLoading } = useSummaryQuery(currentDashboard?.id, period);
  const { data: balances } = useAccountBalancesQuery(currentDashboard?.id, period);

  // Solde affiché : si filtre banque actif, solde de cette banque uniquement, sinon total
  const filteredBalance = bankAccountFilter
    ? balances?.find(b => b.accountId === bankAccountFilter)?.balance ?? 0
    : balances?.reduce((s, b) => s + Number(b.balance), 0) ?? 0;
  const balanceLabel = bankAccountFilter
    ? `Solde ${balances?.find(b => b.accountId === bankAccountFilter)?.bankInstitutionName ?? ''}`
    : 'Solde global';

  const income = Number(summary?.totalIncome ?? 0);
  const expenses = Number(summary?.totalExpenses ?? 0);
  const periodBilan = income - expenses;

  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 md:gap-4">
      <Card label={balanceLabel} value={filteredBalance} highlight color={filteredBalance >= 0 ? 'emerald' : 'red'} loading={isLoading} />
      <Card label={`Revenus ${period.label.toLowerCase()}`} value={income} color="emerald" loading={isLoading} sign="+" />
      <Card label={`Dépenses ${period.label.toLowerCase()}`} value={expenses} color="red" loading={isLoading} sign="-" />
      <Card label={`Bilan ${period.label.toLowerCase()}`} value={periodBilan} color={periodBilan >= 0 ? 'emerald' : 'red'} loading={isLoading} sign={periodBilan >= 0 ? '+' : ''} />
    </div>
  );
};

interface CardProps {
  label: string;
  value: number;
  color: 'emerald' | 'red';
  loading?: boolean;
  highlight?: boolean;
  sign?: string;
}

const Card = ({ label, value, color, loading, highlight, sign }: CardProps) => {
  const colorMap = {
    emerald: 'text-emerald-400 hover:border-emerald-500/30',
    red: 'text-red-400 hover:border-red-500/30',
  };
  return (
    <div className={`bg-white/5 backdrop-blur-xl rounded-2xl border p-4 md:p-5 transition-all duration-200 ${highlight ? 'border-amber-500/30' : 'border-white/10'} ${colorMap[color]}`}>
      <p className="text-white/40 text-xs md:text-sm mb-1 truncate">{label}</p>
      {loading ? (
        <div className="h-8 w-24 rounded bg-white/5 animate-pulse" />
      ) : (
        <p className={`text-xl md:text-2xl lg:text-3xl font-bold ${colorMap[color].split(' ')[0]}`}>
          {sign}{formatCurrency(Math.abs(value))}
        </p>
      )}
    </div>
  );
};
