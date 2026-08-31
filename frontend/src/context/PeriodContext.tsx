import { useMemo, useState } from 'react';
import { DEFAULT_PERIOD, periodsWithCoverage, type Period } from '../utils/periods';
import { PeriodContext } from './period-context';
import { useDashboards } from '../hooks/useDashboards';
import { useCoverageQuery } from '../hooks/queries';

export const PeriodProvider = ({ children }: { children: React.ReactNode }) => {
  const [period, setPeriod] = useState<Period>(DEFAULT_PERIOD);
  const [bankAccountFilter, setBankAccountFilter] = useState<number | undefined>(undefined);
  const [includeExceptional, setIncludeExceptional] = useState(false);

  // « Tout » est borné à la première transaction bancaire du dashboard : avant elle, aucun revenu n'a
  // été importé et le net n'a aucun sens (voir allPeriodSince).
  const { currentDashboard } = useDashboards();
  const { data: coverage } = useCoverageQuery(currentDashboard?.id);
  const periods = useMemo(
    () => periodsWithCoverage(coverage?.firstBankTransactionDate),
    [coverage?.firstBankTransactionDate]
  );

  // La borne arrive après le premier rendu : si « Tout » est déjà sélectionné, il faut la reprendre.
  const periodEffectif = useMemo(
    () => periods.find((p) => p.key === period.key) ?? period,
    [periods, period]
  );

  return (
    <PeriodContext.Provider value={{ period: periodEffectif, setPeriod, periods, bankAccountFilter, setBankAccountFilter, includeExceptional, setIncludeExceptional }}>
      {children}
    </PeriodContext.Provider>
  );
};
