export interface Period {
  key: string;
  label: string;
  /** Compute (from, to) ISO strings */
  range: () => { from: string; to: string };
}

const startOfMonth = (d: Date) => new Date(d.getFullYear(), d.getMonth(), 1);
const endOfMonth = (d: Date) => new Date(d.getFullYear(), d.getMonth() + 1, 0, 23, 59, 59);
const iso = (d: Date) => d.toISOString();

export const PERIODS: Period[] = [
  {
    key: 'this-month',
    label: 'Ce mois-ci',
    range: () => {
      const now = new Date();
      return { from: iso(startOfMonth(now)), to: iso(endOfMonth(now)) };
    },
  },
  {
    key: 'last-month',
    label: 'Mois dernier',
    range: () => {
      const now = new Date();
      const lastMonth = new Date(now.getFullYear(), now.getMonth() - 1, 1);
      return { from: iso(startOfMonth(lastMonth)), to: iso(endOfMonth(lastMonth)) };
    },
  },
  {
    key: 'last-3-months',
    label: '3 derniers mois',
    range: () => {
      const now = new Date();
      const threeMonthsAgo = new Date(now.getFullYear(), now.getMonth() - 2, 1);
      return { from: iso(startOfMonth(threeMonthsAgo)), to: iso(endOfMonth(now)) };
    },
  },
  {
    key: 'this-year',
    label: 'Cette année',
    range: () => {
      const now = new Date();
      return {
        from: new Date(now.getFullYear(), 0, 1).toISOString(),
        to: new Date(now.getFullYear(), 11, 31, 23, 59, 59).toISOString(),
      };
    },
  },
  {
    key: 'all',
    label: 'Tout',
    range: () => ({ from: '', to: '' }),
  },
];

export const periodToRange = (period: Period): { from?: string; to?: string } => {
  const { from, to } = period.range();
  return { from: from || undefined, to: to || undefined };
};

export const DEFAULT_PERIOD = PERIODS[0]; // Ce mois-ci (cohérent avec les budgets mensuels)

const MONTH_LABELS = ['Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin', 'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'];

/** Crée une Period pour un mois précis. yearMonth au format "YYYY-MM" (retour d'un <input type="month">). */
export const makeMonthPeriod = (yearMonth: string): Period => {
  const [yearStr, monthStr] = yearMonth.split('-');
  const year = parseInt(yearStr, 10);
  const month = parseInt(monthStr, 10) - 1; // 0-indexed
  return {
    key: `month-${yearMonth}`,
    label: `${MONTH_LABELS[month]} ${year}`,
    range: () => ({
      from: iso(new Date(year, month, 1)),
      to: iso(new Date(year, month + 1, 0, 23, 59, 59)),
    }),
  };
};
