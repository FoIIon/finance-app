import type { Investment, InvestmentHistoryPoint } from '../../types/investment';
import { formatCurrency } from '../../utils/format';
import { splitHistory } from './portfolioPeriod';
import type { PortfolioPeriod } from './portfolioPeriod';

interface Props {
  investments: Investment[];
  history: InvestmentHistoryPoint[];
  period: PortfolioPeriod;
}

const signed = (v: number) => `${v >= 0 ? '+' : ''}${formatCurrency(v)}`;

export const PortfolioSummary = ({ investments, history, period }: Props) => {
  const active = investments.filter((i) => !i.isArchived);
  const valued = active.filter((i) => i.marketValue != null);

  const totalValue = valued.reduce((s, i) => s + (i.marketValue ?? 0), 0);
  const investedValued = valued.reduce((s, i) => s + i.costBasis, 0);
  const latentGain = totalValue - investedValued;
  const latentPct = investedValued > 0 ? (latentGain / investedValued) * 100 : null;

  // La plus ancienne parmi les dernières valorisations de chaque ligne : c'est elle
  // qui date réellement le total affiché.
  const oldestAsOf = valued.reduce<string | null>((min, i) => {
    if (!i.valuationAsOf) return min;
    return min === null || i.valuationAsOf < min ? i.valuationAsOf : min;
  }, null);

  // Règle d'honnêteté : sans point d'historique daté au plus tard au début de la
  // période, il n'existe aucune baseline légitime, donc aucune variation affichable.
  const { baseline } = splitHistory(history, period);
  const variation = baseline !== null ? totalValue - baseline.value : null;
  const variationPct =
    baseline !== null && baseline.value > 0 ? ((totalValue - baseline.value) / baseline.value) * 100 : null;

  // Des lignes entrées dans la courbe depuis la baseline gonflent la variation :
  // sans cette mention, un apport se lirait comme une performance.
  const lastPoint = history.length > 0 ? history[history.length - 1] : null;
  const includesContributions =
    baseline !== null && lastPoint !== null && lastPoint.linesIncluded > baseline.linesIncluded;

  return (
    <div className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-5">
      <p className="text-sm text-white/50">Valeur du portefeuille</p>
      <div className="mt-1 flex flex-wrap items-baseline gap-x-4 gap-y-1">
        <span aria-label="Valeur totale du portefeuille" className="text-4xl font-bold text-white">
          {formatCurrency(totalValue)}
        </span>
        {variation !== null ? (
          <span
            className="text-lg font-semibold"
            style={{ color: variation >= 0 ? '#34d399' : '#f87171' }}
          >
            {signed(variation)}
            {variationPct !== null && (
              <span className="text-sm font-medium opacity-80 ml-1">
                ({variationPct >= 0 ? '+' : ''}{variationPct.toFixed(1)} %)
              </span>
            )}
            {includesContributions && (
              <span
                className="text-xs font-normal text-white/40 ml-2"
                title="Des lignes ont reçu leur première valorisation pendant la période : leur entrée dans la courbe gonfle la variation, qui ne se lit pas comme une performance"
              >
                apports inclus
              </span>
            )}
          </span>
        ) : (
          <span className="text-lg text-white/30" title="Pas de valorisation au début de cette période">
            —
          </span>
        )}
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-2 text-sm">
        <div>
          <p className="text-white/50">Plus-value latente</p>
          {valued.length > 0 ? (
            <p className="font-semibold" style={{ color: latentGain >= 0 ? '#34d399' : '#f87171' }}>
              {signed(latentGain)}
              {latentPct !== null && (
                <span className="opacity-80 font-medium ml-1">
                  ({latentPct >= 0 ? '+' : ''}{latentPct.toFixed(1)} %)
                </span>
              )}
            </p>
          ) : (
            <p className="text-white/30">—</p>
          )}
        </div>
        <div>
          <p className="text-white/50">Lignes valorisées</p>
          <p className="text-white/90">
            {valued.length} / {active.length}
            {oldestAsOf && (
              <span className="text-white/40 ml-1">
                · plus ancienne au {new Date(oldestAsOf).toLocaleDateString('fr-BE')}
              </span>
            )}
          </p>
        </div>
      </div>
    </div>
  );
};
