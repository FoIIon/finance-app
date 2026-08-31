import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../hooks/usePeriod';
import { useSummaryQuery } from '../../hooks/queries';
import { LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid, ResponsiveContainer, Legend } from 'recharts';
import { formatCurrency } from '../../utils/format';
import type { MonthlyBalance } from '../../types/transaction';

const signe = (v: number) => (v >= 0 ? '+' : '');
const couleurResultat = (v: number) => (v >= 0 ? '#34d399' : '#f87171');

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
            <YAxis yAxisId="flow" stroke="rgba(255,255,255,0.3)" fontSize={11} />
            <YAxis yAxisId="balance" orientation="right" stroke="rgba(255,255,255,0.3)" fontSize={11} />
            <Tooltip
              content={({ active, payload }) => {
                if (!active || !payload || payload.length === 0) return null;
                const p = payload[0].payload as MonthlyBalance;
                // Plus/moins-value du mois = revenus − dépenses, mêmes exclusions que les deux courbes
                const resultat = p.balance;
                // Variation du solde total d'un mois sur l'autre (inclut les mises de côté, absentes du résultat)
                const precedent = data[data.findIndex((m) => m.month === p.month) - 1];
                const variation = precedent ? p.totalBalance - precedent.totalBalance : null;
                return (
                  <div className="bg-[#1a1a3e] border border-white/10 rounded-xl px-3 py-2 text-xs text-white">
                    <p className="text-white/50 mb-1">{p.month}</p>
                    <p style={{ color: '#34d399' }}>Revenus : {formatCurrency(p.income)}</p>
                    <p style={{ color: '#f87171' }}>Dépenses : {formatCurrency(p.expenses)}</p>
                    <p className="mt-1 pt-1 border-t border-white/10 font-semibold" style={{ color: couleurResultat(resultat) }}>
                      Résultat du mois : {signe(resultat)}{formatCurrency(resultat)}
                    </p>
                    <p className="text-white/70">Solde total : {formatCurrency(p.totalBalance)}</p>
                    {variation !== null && (
                      <p style={{ color: couleurResultat(variation) }}>
                        Variation du solde : {signe(variation)}{formatCurrency(variation)}
                      </p>
                    )}
                  </div>
                );
              }}
            />
            <Legend wrapperStyle={{ fontSize: '11px', color: 'rgba(255,255,255,0.6)' }} />
            <Line yAxisId="flow" type="monotone" dataKey="income" stroke="#34d399" strokeWidth={2} dot={{ fill: '#34d399', r: 3 }} name="Revenus" />
            <Line yAxisId="flow" type="monotone" dataKey="expenses" stroke="#f87171" strokeWidth={2} dot={{ fill: '#f87171', r: 3 }} name="Dépenses" />
            <Line yAxisId="balance" type="monotone" dataKey="totalBalance" stroke="#a78bfa" strokeWidth={2} strokeDasharray="4 4" dot={{ fill: '#a78bfa', r: 3 }} name="Solde total" />
          </LineChart>
        </ResponsiveContainer>
      )}
    </div>
  );
};
