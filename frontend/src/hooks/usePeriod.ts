import { useContext } from 'react';
import { PeriodContext } from '../context/period-context';

export const usePeriod = () => useContext(PeriodContext);
