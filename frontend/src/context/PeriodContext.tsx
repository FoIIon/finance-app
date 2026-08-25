import { useState } from 'react';
import { PERIODS, DEFAULT_PERIOD, type Period } from '../utils/periods';
import { PeriodContext } from './period-context';

export const PeriodProvider = ({ children }: { children: React.ReactNode }) => {
  const [period, setPeriod] = useState<Period>(DEFAULT_PERIOD);
  const [bankAccountFilter, setBankAccountFilter] = useState<number | undefined>(undefined);
  const [includeExceptional, setIncludeExceptional] = useState(false);
  return (
    <PeriodContext.Provider value={{ period, setPeriod, periods: PERIODS, bankAccountFilter, setBankAccountFilter, includeExceptional, setIncludeExceptional }}>
      {children}
    </PeriodContext.Provider>
  );
};
