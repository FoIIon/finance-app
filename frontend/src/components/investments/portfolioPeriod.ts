import type { InvestmentHistoryPoint } from '../../types/investment';

export type PortfolioPeriod = '1J' | '1S' | '1M' | '3M' | '6M' | 'YTD' | '1A' | 'MAX';

export const PORTFOLIO_PERIODS: { key: PortfolioPeriod; label: string }[] = [
  { key: '1J', label: '1J' },
  { key: '1S', label: '1S' },
  { key: '1M', label: '1M' },
  { key: '3M', label: '3M' },
  { key: '6M', label: '6M' },
  { key: 'YTD', label: 'YTD' },
  { key: '1A', label: '1A' },
  { key: 'MAX', label: 'Max' },
];

const monthsBack: Record<'1M' | '3M' | '6M' | '1A', number> = {
  '1M': 1,
  '3M': 3,
  '6M': 6,
  '1A': 12,
};

export const periodStart = (period: PortfolioPeriod, now = new Date()): Date | null => {
  if (period === 'MAX') return null;

  // Depuis le 1er janvier de l'année en cours, pas douze mois glissants.
  if (period === 'YTD') return new Date(now.getFullYear(), 0, 1);

  const d = new Date(now);
  if (period === '1J') {
    d.setDate(d.getDate() - 1);
    return d;
  }
  if (period === '1S') {
    d.setDate(d.getDate() - 7);
    return d;
  }

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
  // Un point unique n'est pas une baseline : il EST la valeur courante. Le comparer à
  // lui-même affichait « +0,00 € (+0,0 %) » en vert, soit une stabilité mesurée là où
  // il n'y a aucune mesure.
  if (!start) return { baseline: history.length > 1 ? history[0] : null, points: history };
  const baseline = [...history].reverse().find((p) => new Date(p.asOf) <= start) ?? null;
  const points = history.filter((p) => new Date(p.asOf) > start);
  return { baseline, points };
};
