import { LineChart, Line, YAxis } from 'recharts';
import type { InvestmentValuation } from '../../types/investment';

interface Props {
  /** Valorisations de la ligne, triées par date croissante. */
  valuations: InvestmentValuation[];
  costBasis: number;
}

export const Sparkline = ({ valuations, costBasis }: Props) => {
  if (valuations.length < 2) return null;

  const last = valuations[valuations.length - 1].marketValue;
  const color = last >= costBasis ? '#34d399' : '#f87171';

  return (
    <LineChart width={100} height={28} data={valuations} margin={{ top: 2, right: 2, bottom: 2, left: 2 }}>
      {/* Axe caché : sans lui le domaine partirait de zéro et aplatirait la tendance. */}
      <YAxis hide domain={['dataMin', 'dataMax']} />
      <Line
        type="monotone"
        dataKey="marketValue"
        stroke={color}
        strokeWidth={1.5}
        dot={false}
        isAnimationActive={false}
      />
    </LineChart>
  );
};
