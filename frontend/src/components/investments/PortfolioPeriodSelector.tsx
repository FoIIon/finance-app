import { PORTFOLIO_PERIODS } from './portfolioPeriod';
import type { PortfolioPeriod } from './portfolioPeriod';

interface Props {
  value: PortfolioPeriod;
  onChange: (period: PortfolioPeriod) => void;
}

export const PortfolioPeriodSelector = ({ value, onChange }: Props) => (
  <div className="flex gap-1 bg-white/5 rounded-lg p-1" role="group" aria-label="Période">
    {PORTFOLIO_PERIODS.map((p) => (
      <button
        key={p.key}
        type="button"
        onClick={() => onChange(p.key)}
        className={`px-3 py-1.5 rounded-md text-sm transition-colors ${
          value === p.key ? 'bg-indigo-500 text-white font-medium' : 'text-white/50 hover:text-white'
        }`}
      >
        {p.label}
      </button>
    ))}
  </div>
);
