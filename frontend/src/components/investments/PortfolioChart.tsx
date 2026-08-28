import {
  ComposedChart,
  Area,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
  ResponsiveContainer,
  Legend,
} from 'recharts';
import type { InvestmentHistoryPoint } from '../../types/investment';
import { formatCurrency } from '../../utils/format';
import { splitHistory } from './portfolioPeriod';
import type { PortfolioPeriod } from './portfolioPeriod';

interface Props {
  history: InvestmentHistoryPoint[];
  period: PortfolioPeriod;
  isLoading?: boolean;
}

const fmtDate = (asOf: string) => new Date(asOf).toLocaleDateString('fr-BE');

export const PortfolioChart = ({ history, period, isLoading }: Props) => {
  const { baseline, points } = splitHistory(history, period);

  // Le trait porte la performance de la période affichée, comme la courbe d'un actif :
  // vert si le patrimoine a monté depuis le premier point visible, rouge sinon.
  const serie = points.length > 0 ? points : history;
  const monte = serie.length < 2 || serie[serie.length - 1].value >= serie[0].value;
  const couleur = monte ? '#34d399' : '#f87171';
  // Le point d'ancrage évite une courbe qui semble naître de rien en début de période.
  // En Max, la baseline est déjà le premier point : ne pas le dupliquer.
  const data = period === 'MAX' ? points : baseline !== null ? [baseline, ...points] : points;
  const last = data.length > 0 ? data[data.length - 1] : null;

  return (
    <div className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-5">
      <h3 className="text-base md:text-lg font-semibold text-white mb-4">Évolution du patrimoine</h3>
      {isLoading ? (
        <div className="h-[280px] rounded bg-white/5 animate-pulse" />
      ) : data.length < 2 ? (
        <p className="text-white/30 text-center py-12 text-sm">Pas assez d'historique sur cette période</p>
      ) : (
        <>
          <ResponsiveContainer width="100%" height={280}>
            <ComposedChart data={data} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
              <defs>
                <linearGradient id="portfolioValueGradient" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={couleur} stopOpacity={0.25} />
                  <stop offset="100%" stopColor={couleur} stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.05)" />
              <XAxis dataKey="asOf" tickFormatter={fmtDate} stroke="rgba(255,255,255,0.3)" fontSize={11} />
              <YAxis stroke="rgba(255,255,255,0.3)" fontSize={11} />
              <Tooltip
                content={({ active, payload }) => {
                  if (!active || !payload || payload.length === 0) return null;
                  const p = payload[0].payload as InvestmentHistoryPoint;
                  // Investi inconnu sur les points portés par la série réelle Trade Republic :
                  // on n'affiche pas un écart calculé sur un chiffre qu'on n'a pas.
                  const gap = p.invested != null ? p.value - p.invested : null;
                  return (
                    <div className="bg-[#1a1a3e] border border-white/10 rounded-xl px-3 py-2 text-xs text-white">
                      <p className="text-white/50 mb-1">{fmtDate(p.asOf)}</p>
                      <p>Valeur : {formatCurrency(p.value)}</p>
                      {p.invested != null && gap != null && (
                        <>
                          <p className="text-white/70">
                            {p.reconstructed ? 'Investi net' : 'Investi'} : {formatCurrency(p.invested)}
                          </p>
                          <p style={{ color: gap >= 0 ? '#34d399' : '#f87171' }}>
                            {p.reconstructed ? 'Résultat total' : 'Écart'} : {gap >= 0 ? '+' : ''}{formatCurrency(gap)}
                          </p>
                        </>
                      )}
                    </div>
                  );
                }}
              />
              <Legend wrapperStyle={{ fontSize: '11px', color: 'rgba(255,255,255,0.6)' }} />
              <Area
                type="monotone"
                dataKey="value"
                name="Valeur"
                stroke={couleur}
                strokeWidth={1.5}
                fill="url(#portfolioValueGradient)"
                dot={false}
                activeDot={{ r: 4, strokeWidth: 0 }}
              />
              <Line
                type="monotone"
                dataKey="invested"
                name="Investi"
                stroke="rgba(255,255,255,0.45)"
                strokeWidth={2}
                strokeDasharray="5 5"
                dot={false}
              />
            </ComposedChart>
          </ResponsiveContainer>
          {last !== null && last.linesIncluded < last.linesTotal && (
            <p className="text-xs text-white/40 mt-2">
              Courbe partielle : {last.linesIncluded} lignes sur {last.linesTotal} valorisées
            </p>
          )}
        </>
      )}
    </div>
  );
};
