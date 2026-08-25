import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { useQuery } from '@tanstack/react-query';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
  ResponsiveContainer,
  ReferenceLine,
} from 'recharts';
import { investmentsApi } from '../../api/investments';
import { PortfolioPeriodSelector } from './PortfolioPeriodSelector';
import { periodStart } from './portfolioPeriod';
import type { PortfolioPeriod } from './portfolioPeriod';
import type { Investment } from '../../types/investment';
import { formatCurrency, formatPercent } from '../../utils/format';


interface Props {
  investment: Investment;
  onClose: () => void;
}

export const InvestmentDetail = ({ investment, onClose }: Props) => {
  const { data: valuations, isLoading } = useQuery({
    queryKey: ['investment-line-valuations', investment.id],
    queryFn: async () => {
      const res = await investmentsApi.getValuations(investment.id);
      return res.data;
    },
  });

  const [period, setPeriod] = useState<PortfolioPeriod>('6M');

  // L'API renvoie l'historique en ordre décroissant : la courbe le veut croissant.
  const ascending = useMemo(
    () => [...(valuations ?? [])].sort((a, b) => a.asOf.localeCompare(b.asOf)),
    [valuations]
  );

  const shown = useMemo(() => {
    const start = periodStart(period);
    if (!start) return ascending;
    return ascending.filter((v) => new Date(v.asOf) >= start);
  }, [ascending, period]);

  // Le trait porte la performance de la période affichée : vert si la ligne a monté
  // depuis le premier point visible, rouge sinon. Changer de période change la couleur,
  // et c'est voulu : la couleur décrit ce que le graphique montre.
  const variationPeriode = shown.length >= 2
    ? shown[shown.length - 1].marketValue - shown[0].marketValue
    : null;
  const variationPct = variationPeriode !== null && shown[0].marketValue > 0
    ? (variationPeriode / shown[0].marketValue) * 100
    : null;
  const couleurTrait = variationPeriode === null || variationPeriode >= 0 ? '#34d399' : '#f87171';

  useEffect(() => {
    const onEsc = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', onEsc);
    return () => document.removeEventListener('keydown', onEsc);
  }, [onClose]);

  const modal = (
    <div
      role="dialog"
      aria-modal="true"
      className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-end md:items-center justify-center"
      onClick={onClose}
    >
      <div
        className="bg-[#1a1a3e] rounded-t-2xl md:rounded-2xl border border-white/10 p-6 md:p-8 w-full md:max-w-2xl max-h-[85vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between mb-4">
          <div>
            <h3 className="text-xl font-bold text-white">{investment.name}</h3>
            <p className="text-white/50 text-sm mt-1">{investment.holder}</p>

            <div className="mt-3 flex flex-wrap gap-x-6 gap-y-2 text-sm">
              <div>
                <span className="text-white/50">Valeur </span>
                <span className="text-white font-semibold">
                  {investment.marketValue != null ? formatCurrency(investment.marketValue) : '—'}
                </span>
              </div>
              <div>
                <span className="text-white/50">Plus-value </span>
                {investment.gainAmount != null ? (
                  <span
                    className="font-semibold"
                    style={{ color: investment.gainAmount >= 0 ? '#34d399' : '#f87171' }}
                  >
                    {investment.gainAmount >= 0 ? '+' : ''}{formatCurrency(investment.gainAmount)}
                    {investment.gainPercent != null && (
                      <span className="opacity-80 font-medium ml-1">
                        ({investment.gainPercent >= 0 ? '+' : ''}{formatPercent(investment.gainPercent)} %)
                      </span>
                    )}
                  </span>
                ) : (
                  <span className="text-white/30">—</span>
                )}
              </div>
              <div>
                <span className="text-white/50">Investi </span>
                <span className="text-white/90">{formatCurrency(investment.costBasis)}</span>
              </div>
              <div>
                <span className="text-white/50">PRU </span>
                <span className="text-white/90">
                  {investment.unitCost != null ? formatCurrency(investment.unitCost) : '—'}
                </span>
              </div>
              <div>
                <span className="text-white/50">Quantité </span>
                <span className="text-white/90">
                  {investment.quantity.toLocaleString('fr-BE', { maximumFractionDigits: 6 })}
                </span>
              </div>
            </div>
          </div>
          <button
            onClick={onClose}
            aria-label="Fermer"
            className="text-white/40 hover:text-white text-2xl leading-none px-2"
          >
            ×
          </button>
        </div>

        {isLoading ? (
          <div className="h-[200px] rounded bg-white/5 animate-pulse mb-4" />
        ) : ascending.length >= 2 ? (
          <div className="mb-4">
            <ResponsiveContainer width="100%" height={200}>
              <LineChart data={shown} margin={{ top: 12, right: 8, left: 0, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.05)" />
                <XAxis
                  dataKey="asOf"
                  tickFormatter={(v: string) => new Date(v).toLocaleDateString('fr-BE')}
                  stroke="rgba(255,255,255,0.3)"
                  fontSize={11}
                />
                <YAxis stroke="rgba(255,255,255,0.3)" fontSize={11} />
                <Tooltip
                  contentStyle={{
                    backgroundColor: '#1a1a3e',
                    border: '1px solid rgba(255,255,255,0.1)',
                    borderRadius: '12px',
                    color: '#fff',
                  }}
                  labelFormatter={(v) => new Date(v as string).toLocaleDateString('fr-BE')}
                  formatter={(value) => [formatCurrency(value as number), 'Valeur']}
                />
                {/* Au-dessus de la ligne « Investi » la position est gagnante, en dessous
                    perdante : lisible d'un coup d'œil. extendDomain garde la ligne visible
                    même quand toutes les valorisations sont au-dessus du coût. */}
                <ReferenceLine
                  y={investment.costBasis}
                  stroke="rgba(255,255,255,0.45)"
                  strokeDasharray="4 4"
                  ifOverflow="extendDomain"
                  label={{
                    value: 'Investi',
                    position: 'insideBottomRight',
                    fill: 'rgba(255,255,255,0.5)',
                    fontSize: 10,
                  }}
                />
                <Line
                  type="monotone"
                  dataKey="marketValue"
                  stroke={couleurTrait}
                  strokeWidth={1.25}
                  dot={false}
                  activeDot={{ r: 3, strokeWidth: 0 }}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        ) : (
          <p className="text-white/30 text-center py-6 text-sm mb-4">
            Pas assez de valorisations pour tracer une courbe
          </p>
        )}

        <div className="flex flex-wrap items-center justify-center gap-3">
          {variationPeriode !== null && (
            <span className="text-sm" style={{ color: couleurTrait }}>
              {variationPeriode >= 0 ? '+' : ''}{formatCurrency(variationPeriode)}
              {variationPct !== null && (
                <span className="opacity-80 ml-1">
                  ({variationPct >= 0 ? '+' : ''}{formatPercent(variationPct)} %)
                </span>
              )}
              <span className="text-white/40 ml-1">sur la période</span>
            </span>
          )}
          <PortfolioPeriodSelector value={period} onChange={setPeriod} />
        </div>

        <p className="mt-4 text-xs text-white/40 bg-white/5 rounded-lg px-3 py-2">
          Mouvements (achats, ventes, dividendes) : disponibles avec l'intégration Trade Republic
        </p>
      </div>
    </div>
  );

  return createPortal(modal, document.body);
};
