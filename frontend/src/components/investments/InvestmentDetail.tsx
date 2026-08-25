import { useEffect, useMemo } from 'react';
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
import { ValuationSource } from '../../types/investment';
import type { Investment } from '../../types/investment';
import { formatCurrency } from '../../utils/format';

const sourceLabels: Record<number, string> = {
  [ValuationSource.Manual]: 'Manuelle',
  [ValuationSource.TradeRepublic]: 'Trade Republic',
  [ValuationSource.SpotApi]: 'Cours spot',
};

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

  // L'API renvoie l'historique en ordre décroissant : la courbe le veut croissant.
  const ascending = useMemo(
    () => [...(valuations ?? [])].sort((a, b) => a.asOf.localeCompare(b.asOf)),
    [valuations]
  );

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
            <p className="text-white/50 text-sm mt-1">
              {investment.holder} · investi {formatCurrency(investment.costBasis)}
            </p>
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
              <LineChart data={ascending} margin={{ top: 12, right: 8, left: 0, bottom: 0 }}>
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
                  stroke="#6366f1"
                  strokeWidth={2}
                  dot={{ fill: '#6366f1', r: 3, strokeWidth: 0 }}
                  activeDot={{ r: 4 }}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        ) : (
          <p className="text-white/30 text-center py-6 text-sm mb-4">
            Pas assez de valorisations pour tracer une courbe
          </p>
        )}

        <div className="flex-1 overflow-y-auto -mx-2 px-2">
          {isLoading ? (
            <div className="space-y-2">
              {[1, 2, 3].map((i) => (
                <div key={i} className="h-10 rounded bg-white/5 animate-pulse" />
              ))}
            </div>
          ) : !valuations || valuations.length === 0 ? (
            <p className="text-white/30 text-center py-8 text-sm">Aucune valorisation</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="text-white/50 border-b border-white/10">
                <tr>
                  <th className="text-left p-2">Date</th>
                  <th className="text-right p-2">Valeur</th>
                  <th className="text-left p-2">Source</th>
                </tr>
              </thead>
              <tbody>
                {valuations.map((v) => (
                  <tr key={v.id} className="border-b border-white/5 text-white/90">
                    <td className="p-2">{new Date(v.asOf).toLocaleDateString('fr-BE')}</td>
                    <td className="p-2 text-right">{formatCurrency(v.marketValue)}</td>
                    <td className="p-2 text-white/60">{sourceLabels[v.source]}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        <p className="mt-4 text-xs text-white/40 bg-white/5 rounded-lg px-3 py-2">
          Mouvements (achats, ventes, dividendes) : disponibles avec l'intégration Trade Republic
        </p>
      </div>
    </div>
  );

  return createPortal(modal, document.body);
};
