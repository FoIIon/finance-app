import type { InvestmentHistoryPoint } from '../../types/investment';

export type PortfolioPeriod = '1M' | '3M' | '6M' | '1A' | 'MAX';

export const PORTFOLIO_PERIODS: { key: PortfolioPeriod; label: string }[] = [
  { key: '1M', label: '1M' },
  { key: '3M', label: '3M' },
  { key: '6M', label: '6M' },
  { key: '1A', label: '1A' },
  { key: 'MAX', label: 'Max' },
];

const monthsBack: Record<Exclude<PortfolioPeriod, 'MAX'>, number> = {
  '1M': 1,
  '3M': 3,
  '6M': 6,
  '1A': 12,
};

export const periodStart = (period: PortfolioPeriod, now = new Date()): Date | null => {
  if (period === 'MAX') return null;
  const d = new Date(now);
  d.setMonth(d.getMonth() - monthsBack[period]);
  return d;
};

/**
 * Découpe l'historique pour une période. `baseline` est le dernier point daté au plus
 * tard au début de la période (valeur reportée) : c'est lui, et lui seul, qui autorise
 * l'affichage d'une variation honnête. Il sert aussi de point d'ancrage à la courbe.
 * En Max, la baseline est le tout premier point.
 */
export const splitHistory = (history: InvestmentHistoryPoint[], period: PortfolioPeriod) => {
  const start = periodStart(period);
  if (!start) return { baseline: history[0] ?? null, points: history };
  const baseline = [...history].reverse().find((p) => new Date(p.asOf) <= start) ?? null;
  const points = history.filter((p) => new Date(p.asOf) > start);
  return { baseline, points };
};
