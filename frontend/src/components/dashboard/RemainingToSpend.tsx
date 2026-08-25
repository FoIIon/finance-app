import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  ReferenceLine,
  ReferenceDot,
  ResponsiveContainer,
} from 'recharts';
import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../hooks/usePeriod';
import { useSummaryQuery } from '../../hooks/queries';
import { transactionsApi } from '../../api/transactions';
import { formatCurrency } from '../../utils/format';

/**
 * Résout l'année/mois d'une période « mois précis » (this-month, last-month, month-YYYY-MM).
 * Renvoie null pour les périodes multi-mois (3 mois, année, tout) → pas de burn-down.
 * On dérive du KEY, jamais du range ISO (toISOString décale le 1er du mois en UTC).
 */
const resolveMonth = (key: string): { year: number; month: number } | null => {
  const now = new Date();
  if (key === 'this-month') return { year: now.getFullYear(), month: now.getMonth() + 1 };
  if (key === 'last-month') {
    const d = new Date(now.getFullYear(), now.getMonth() - 1, 1);
    return { year: d.getFullYear(), month: d.getMonth() + 1 };
  }
  const m = key.match(/^month-(\d{4})-(\d{2})$/);
  if (m) return { year: parseInt(m[1], 10), month: parseInt(m[2], 10) };
  return null;
};

/**
 * Reste à vivre = entrées − dépenses (période sélectionnée).
 * Sur un mois précis : burn-down jour par jour du reste + projection de fin de mois
 * (remplace la note manuelle d'Audrey). Sinon : reste simple basé sur le summary.
 */
export const RemainingToSpend = () => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const { data: summary, isLoading } = useSummaryQuery(currentDashboard?.id, period);

  const ym = resolveMonth(period.key);
  const dashboardId = currentDashboard?.id;

  const { data: burndown, isLoading: burndownLoading } = useQuery({
    queryKey: ['burndown', dashboardId, ym?.year, ym?.month],
    enabled: !!dashboardId && !!ym,
    queryFn: async () => {
      const res = await transactionsApi.getBurndown(dashboardId, ym!.year, ym!.month);
      return res.data;
    },
  });

  const income = Number(summary?.totalIncome ?? 0);
  const spent = Number(summary?.totalExpenses ?? 0);
  // Headline : remainingToday sur un mois précis (une fois le burn-down chargé), sinon reste du summary
  const remaining = ym && burndown ? burndown.remainingToday : income - spent;

  const projectionPositive = (burndown?.projectedEndOfMonth ?? 0) >= 0;
  const projectionColor = projectionPositive ? '#34d399' : '#f87171';

  // Série du graphe : reste réel (jusqu'à aujourd'hui) + segment de projection pointillé (mois courant)
  const chartData = useMemo(() => {
    if (!burndown) return [];
    const lastDay = burndown.days.length;
    const j = burndown.todayDay;
    const points = burndown.days.map((d) => ({
      day: d.day,
      remaining: d.remaining,
      projection: null as number | null,
    }));
    if (!burndown.isPast && j != null) {
      const span = lastDay - j;
      const startVal = burndown.remainingToday;
      const endVal = burndown.projectedEndOfMonth;
      for (const p of points) {
        if (p.day >= j) {
          p.projection = span > 0 ? startVal + (endVal - startVal) * ((p.day - j) / span) : startVal;
        }
      }
    }
    return points;
  }, [burndown]);

  // Point « aujourd'hui » (mois courant) ou point final (mois passé)
  const markerDay = burndown ? (burndown.todayDay ?? burndown.days.length) : null;
  const markerValue = burndown?.remainingToday ?? 0;

  const showChart = !!ym && !!burndown && chartData.length > 0;

  let projectionLine: string | null = null;
  if (burndown && ym && !burndown.isPast) {
    const hors = burndown.recurringIncluded ? '' : ' (hors récurrentes)';
    projectionLine = `Projection fin de mois : ${formatCurrency(burndown.projectedEndOfMonth)}${hors}`;
  }

  return (
    <div className="bg-gradient-to-br from-amber-500/10 to-orange-600/10 backdrop-blur-xl rounded-2xl border border-amber-500/30 p-5 md:p-6">
      <p className="text-amber-300/60 text-xs md:text-sm uppercase tracking-wider mb-1">Reste à vivre</p>
      {isLoading ? (
        <div className="h-12 w-32 rounded bg-white/5 animate-pulse" />
      ) : (
        <p className={`text-3xl md:text-4xl font-bold ${remaining >= 0 ? 'text-emerald-300' : 'text-red-300'}`}>
          {remaining >= 0 ? '+' : ''}{formatCurrency(remaining)}
        </p>
      )}
      <p className="text-white/50 text-xs mt-2">
        {formatCurrency(income)} revenus − {formatCurrency(spent)} dépenses
      </p>

      {ym && burndownLoading && (
        <div className="h-[120px] mt-3 rounded bg-white/5 animate-pulse" />
      )}

      {showChart && (
        <div className="h-[120px] mt-3 -mx-1">
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={chartData} margin={{ top: 6, right: 6, left: 0, bottom: 0 }}>
              <XAxis dataKey="day" stroke="rgba(255,255,255,0.25)" fontSize={11} tickLine={false} axisLine={false} minTickGap={24} />
              <YAxis stroke="rgba(255,255,255,0.25)" fontSize={11} tickLine={false} axisLine={false} width={44} tickFormatter={(v) => formatCurrency(v as number)} />
              <ReferenceLine y={0} stroke="rgba(255,255,255,0.35)" strokeDasharray="3 3" />
              <Tooltip
                contentStyle={{ backgroundColor: '#1a1a3e', border: '1px solid rgba(255,255,255,0.1)', borderRadius: '12px', color: '#fff', fontSize: '12px' }}
                labelFormatter={(v) => `Jour ${v}`}
                formatter={(value, name) => [formatCurrency(value as number), name === 'projection' ? 'Projection' : 'Reste']}
              />
              <Line type="monotone" dataKey="remaining" stroke="#fbbf24" strokeWidth={2} dot={false} connectNulls={false} name="remaining" isAnimationActive={false} />
              <Line type="monotone" dataKey="projection" stroke={projectionColor} strokeWidth={2} strokeDasharray="4 4" dot={false} connectNulls name="projection" isAnimationActive={false} />
              {markerDay != null && (
                <ReferenceDot x={markerDay} y={markerValue} r={3.5} fill="#fbbf24" stroke="#1a1a3e" strokeWidth={1.5} />
              )}
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}

      {projectionLine && (
        <p className={`text-xs mt-2 italic ${projectionPositive ? 'text-emerald-200/60' : 'text-red-200/60'}`}>{projectionLine}</p>
      )}
    </div>
  );
};
