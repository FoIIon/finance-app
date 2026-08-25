import { usePeriod } from '../../hooks/usePeriod';
import { makeMonthPeriod } from '../../utils/periods';

export const PeriodSelector = () => {
  const { period, setPeriod, periods } = usePeriod();

  // Si la period courante est un mois précis, son key commence par "month-YYYY-MM"
  const customMonthValue = period.key.startsWith('month-') ? period.key.slice(6) : '';

  return (
    <div className="flex items-center gap-2 flex-wrap">
      <span className="text-white/40 text-sm hidden md:inline">Période :</span>
      <div className="flex gap-1 p-1 rounded-xl bg-white/5 border border-white/10">
        {periods.map((p) => (
          <button
            key={p.key}
            onClick={() => setPeriod(p)}
            className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-all ${
              period.key === p.key
                ? 'bg-amber-500/20 text-amber-300 border border-amber-500/30'
                : 'text-white/60 hover:text-white hover:bg-white/5'
            }`}
          >
            {p.label}
          </button>
        ))}
      </div>
      <input
        type="month"
        value={customMonthValue}
        onChange={(e) => {
          if (e.target.value) setPeriod(makeMonthPeriod(e.target.value));
        }}
        className={`px-3 py-1.5 rounded-xl text-sm font-medium bg-white/5 border transition-all [color-scheme:dark] ${
          customMonthValue
            ? 'border-amber-500/30 text-amber-300'
            : 'border-white/10 text-white/60 hover:text-white'
        }`}
        title="Sélectionner un mois précis"
      />
    </div>
  );
};
