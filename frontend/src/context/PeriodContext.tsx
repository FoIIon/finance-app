import { createContext, useContext, useState } from 'react';
import { PERIODS, DEFAULT_PERIOD, type Period } from '../utils/periods';

interface PeriodContextType {
  period: Period;
  setPeriod: (p: Period) => void;
  periods: Period[];
  /** Filtre par BankAccountId (compte bancaire physique). undefined = tous les comptes. */
  bankAccountFilter?: number;
  setBankAccountFilter: (id: number | undefined) => void;
}

export const PeriodContext = createContext<PeriodContextType>({
  period: DEFAULT_PERIOD,
  setPeriod: () => {},
  periods: PERIODS,
  bankAccountFilter: undefined,
  setBankAccountFilter: () => {},
});

export const PeriodProvider = ({ children }: { children: React.ReactNode }) => {
  const [period, setPeriod] = useState<Period>(DEFAULT_PERIOD);
  const [bankAccountFilter, setBankAccountFilter] = useState<number | undefined>(undefined);
  return (
    <PeriodContext.Provider value={{ period, setPeriod, periods: PERIODS, bankAccountFilter, setBankAccountFilter }}>
      {children}
    </PeriodContext.Provider>
  );
};

export const usePeriod = () => useContext(PeriodContext);
