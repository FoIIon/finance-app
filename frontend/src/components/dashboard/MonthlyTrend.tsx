import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../context/PeriodContext';
import { useSummaryQuery } from '../../hooks/queries';
import { LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid, ResponsiveContainer } from 'recharts';
import { formatCurrency } from '../../utils/format';

export const MonthlyTrend = () => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const { data: summary, isLoading } = useSummaryQuery(currentDashboard?.id, period);

  const data = summary?.monthlyBalance ?? [];

  return (
    <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
      <h3 className="text-base md:text-lg font-semibold text-white mb-4">Évolution sur 6 mois</h3>
      {isLoading ? (
        <div className="h-[250px] rounded bg-white/5 animate-pulse" />
      ) : data.length === 0 ? (
        <p className="text-white/30 text-center py-12 text-sm">Aucune donnée</p>
      ) : (
        <ResponsiveContainer width="100%" height={250}>
          <LineChart data={data}>
            <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.05)" />
            <XAxis dataKey="month" stroke="rgba(255,255,255,0.3)" fontSize={11} />
            <YAxis stroke="rgba(255,255,255,0.3)" fontSize={11} />
            <Tooltip
              contentStyle={{ backgroundColor: '#1a1a3e', border: '1px solid rgba(255,255,255,0.1)', borderRadius: '12px', color: '#fff' }}
              formatter={(value) => formatCurrency(value as number)}
            />
            <Line type="monotone" dataKey="income" stroke="#34d399" strokeWidth={2} dot={{ fill: '#34d399', r: 3 }} name="Revenus" />
            <Line type="monotone" dataKey="expenses" stroke="#f87171" strokeWidth={2} dot={{ fill: '#f87171', r: 3 }} name="Dépenses" />
          </LineChart>
        </ResponsiveContainer>
      )}
    </div>
  );
};
