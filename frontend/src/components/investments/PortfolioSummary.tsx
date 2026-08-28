import type { Investment, InvestmentHistoryPoint } from '../../types/investment';
import { formatCurrency, formatPercent } from '../../utils/format';
import { useCashQuery } from '../../hooks/queries';
import { splitHistory } from './portfolioPeriod';
import type { PortfolioPeriod } from './portfolioPeriod';

interface Props {
  investments: Investment[];
  history: InvestmentHistoryPoint[];
  period: PortfolioPeriod;
}

const signed = (v: number) => `${v >= 0 ? '+' : ''}${formatCurrency(v)}`;

export const PortfolioSummary = ({ investments, history, period }: Props) => {
  const { data: cash } = useCashQuery();
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
  const { baseline, points } = splitHistory(history, period);
  const lastPoint = history.length > 0 ? history[history.length - 1] : null;

  // Sur une série rebâtie depuis la timeline, la valeur bouge surtout par les apports : comparer
  // la valeur d'aujourd'hui au premier point (200 € au premier ordre) affichait +8 000 %. La
  // variation d'une période est donc celle du RÉSULTAT (valeur − investi net), apports neutralisés,
  // rapportée à l'investi net en début de période. Les deux bornes viennent de la même série.
  const surResultat =
    baseline !== null && lastPoint !== null && baseline.reconstructed && lastPoint.reconstructed
    && baseline.invested != null && lastPoint.invested != null;
  const variation = surResultat
    ? (lastPoint!.value - lastPoint!.invested!) - (baseline!.value - baseline!.invested!)
    : baseline !== null ? totalValue - baseline.value : null;
  // Pourcentage : Modified Dietz. Rapporter le gain à l'investi de départ donnait +217 % en Max,
  // parce que le capital du 24/11/2023 était petit et que tout le reste est arrivé par apports.
  // Dénominateur = capital de départ + chaque apport (variation d'investi net entre deux points)
  // pondéré par la fraction de période qu'il a passée investi. C'est le rendement du capital
  // réellement exposé, la mesure standard d'un portefeuille avec flux.
  const dietz = ((): number | null => {
    if (!surResultat || variation == null) return null;
    const serie = [baseline!, ...points.filter((p) => p.reconstructed && p.invested != null)];
    if (serie.length < 2) return null;
    const t0 = new Date(serie[0].asOf).getTime();
    const t1 = new Date(serie[serie.length - 1].asOf).getTime();
    if (t1 <= t0) return null;
    let denominateur = serie[0].value;
    for (let i = 1; i < serie.length; i++) {
      const flux = serie[i].invested! - serie[i - 1].invested!;
      if (flux === 0) continue;
      const poids = (t1 - new Date(serie[i].asOf).getTime()) / (t1 - t0);
      denominateur += flux * poids;
    }
    return denominateur > 0 ? (variation / denominateur) * 100 : null;
  })();
  const variationPct = surResultat
    ? dietz
    : baseline !== null && baseline.value > 0 ? ((totalValue - baseline.value) / baseline.value) * 100 : null;

  // Des lignes entrées dans la courbe depuis la baseline gonflent la variation :
  // sans cette mention, un apport se lirait comme une performance.
  const includesContributions =
    !surResultat && baseline !== null && lastPoint !== null && lastPoint.linesIncluded > baseline.linesIncluded;

  // Résultat total, ventes comprises : dernier point rebâti depuis la timeline TR. Valeur moins
  // investi net (achats − ventes). C'est le chiffre que la plus-value latente ne donne pas :
  // une position vendue à perte y pèse, alors qu'elle disparaît de la latente.
  const reconstructed = [...history].reverse().find((p) => p.reconstructed && p.invested != null) ?? null;
  const totalResult = reconstructed && reconstructed.invested != null ? reconstructed.value - reconstructed.invested : null;
  const totalResultPct =
    reconstructed && reconstructed.invested != null && reconstructed.invested > 0 && totalResult != null
      ? (totalResult / reconstructed.invested) * 100
      : null;

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
                ({variationPct >= 0 ? '+' : ''}{formatPercent(variationPct)} %)
              </span>
            )}
            {surResultat && (
              <span
                className="text-xs font-normal text-white/40 ml-2"
                title="Gain de la période (valeur moins investi net, apports et retraits neutralisés). Le pourcentage rapporte ce gain au capital moyen exposé sur la période, chaque apport pondéré par le temps qu'il a passé investi (Modified Dietz)"
              >
                hors apports
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

      <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4 text-sm">
        <div>
          <p className="text-white/50">Résultat total, ventes comprises</p>
          {totalResult !== null ? (
            <p
              className="font-semibold"
              style={{ color: totalResult >= 0 ? '#34d399' : '#f87171' }}
              title={`Valeur ${formatCurrency(reconstructed!.value)} moins investi net ${formatCurrency(reconstructed!.invested!)} (achats − ventes depuis le premier ordre), au ${new Date(reconstructed!.asOf).toLocaleDateString('fr-BE')}`}
            >
              {signed(totalResult)}
              {totalResultPct !== null && (
                <span className="opacity-80 font-medium ml-1">
                  ({totalResultPct >= 0 ? '+' : ''}{formatPercent(totalResultPct)} %)
                </span>
              )}
            </p>
          ) : (
            <p className="text-white/30" title="Disponible après un import Trade Republic">—</p>
          )}
        </div>
        <div>
          <p className="text-white/50">Plus-value latente</p>
          {valued.length > 0 ? (
            <p className="font-semibold" style={{ color: latentGain >= 0 ? '#34d399' : '#f87171' }}>
              {signed(latentGain)}
              {latentPct !== null && (
                <span className="opacity-80 font-medium ml-1">
                  ({latentPct >= 0 ? '+' : ''}{formatPercent(latentPct)} %)
                </span>
              )}
            </p>
          ) : (
            <p className="text-white/30">—</p>
          )}
        </div>
        <div>
          {/* Volontairement hors de la valeur du portefeuille et de la plus-value : ce
              n'est pas un actif dont on mesure la performance. */}
          <p className="text-white/50">Espèces sur le compte</p>
          {cash?.amount != null ? (
            <p className="text-white/90 font-semibold">
              {formatCurrency(cash.amount)}
              {cash.updatedAt && (
                <span className="text-white/40 font-normal ml-1">
                  · au {new Date(cash.updatedAt).toLocaleDateString('fr-BE')}
                </span>
              )}
            </p>
          ) : (
            <p className="text-white/30" title="Relevé au prochain import Trade Republic">—</p>
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
