import { useMemo, useState } from 'react';
import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../hooks/usePeriod';
import { useSummaryQuery, useCategoriesQuery } from '../../hooks/queries';
import { formatCurrency } from '../../utils/format';
import type { CategoryBreakdown } from '../../types/transaction';
import { CategoryFlowModal } from '../../components/dashboard/CategoryFlowModal';

/**
 * Une catégorie, ses deux sens et son net. Les autres onglets montrent les rentrées d'un côté et les
 * dépenses de l'autre, jamais les deux face à face, alors qu'une catégorie porte souvent les deux :
 * Enfants encaisse les allocations et paie la crèche, Santé paie le médecin et reçoit la mutuelle,
 * Sorties avance des places de foot et se fait rembourser.
 *
 * Les transferts sont séparés en deux colonnes, parce qu'ils ne se lisent pas pareil :
 *  - « Mises de côté » = l'argent qu'on a décidé de mettre de côté dans le mois (achat de titres).
 *    Il sort du compte courant, il compte donc dans le net, comme dans le bilan d'Audrey.
 *  - « Hors bilan » = les mouvements dont le montant ne se décide pas dans le mois : balayage
 *    automatique du compte joint vers le livret, alimentation de la carte Trade Republic. Affichés
 *    pour information, jamais soustraits — leur contrepartie est déjà comptée ailleurs.
 */
interface FlowRow {
  categoryId: number;
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  entrees: number;
  sorties: number;
  misesDeCote: number;
  horsBilan: number;
  net: number;
}

type FlowField = 'entrees' | 'sorties' | 'misesDeCote' | 'horsBilan';

const vide = (c: CategoryBreakdown): FlowRow => ({
  categoryId: c.categoryId,
  categoryName: c.categoryName,
  categoryIcon: c.categoryIcon,
  categoryColor: c.categoryColor,
  entrees: 0,
  sorties: 0,
  misesDeCote: 0,
  horsBilan: 0,
  net: 0,
});

const fold = (
  rows: Map<number, FlowRow>,
  items: CategoryBreakdown[],
  champ: FlowField | ((c: CategoryBreakdown) => FlowField)
) => {
  for (const c of items) {
    const ligne = rows.get(c.categoryId) ?? vide(c);
    ligne[typeof champ === 'function' ? champ(c) : champ] += Number(c.amount);
    rows.set(c.categoryId, ligne);
  }
};

const DashboardFlows = () => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const { data: summary, isLoading } = useSummaryQuery(currentDashboard?.id, period);
  const { data: categories } = useCategoriesQuery();
  const [selectedId, setSelectedId] = useState<number | null>(null);

  // Quelles catégories de transfert sortent du bilan (balayage, virements internes).
  const horsBilanIds = useMemo(
    () => new Set((categories ?? []).filter((c) => c.excludeFromMonthlyReport).map((c) => c.id)),
    [categories]
  );

  const rows = useMemo(() => {
    const map = new Map<number, FlowRow>();
    fold(map, summary?.incomeBreakdown ?? [], 'entrees');
    fold(map, summary?.categoryBreakdown ?? [], 'sorties');
    fold(map, summary?.savingsBreakdown ?? [], (c) => (horsBilanIds.has(c.categoryId) ? 'horsBilan' : 'misesDeCote'));
    return [...map.values()]
      .map((r) => ({ ...r, net: r.entrees - r.sorties - r.misesDeCote }))
      .sort((a, b) => Math.abs(b.net) - Math.abs(a.net) || Math.abs(b.horsBilan) - Math.abs(a.horsBilan));
  }, [summary, horsBilanIds]);

  const totaux = useMemo(
    () =>
      rows.reduce(
        (acc, r) => ({
          entrees: acc.entrees + r.entrees,
          sorties: acc.sorties + r.sorties,
          misesDeCote: acc.misesDeCote + r.misesDeCote,
          horsBilan: acc.horsBilan + r.horsBilan,
          net: acc.net + r.net,
        }),
        { entrees: 0, sorties: 0, misesDeCote: 0, horsBilan: 0, net: 0 }
      ),
    [rows]
  );

  const selected = rows.find((r) => r.categoryId === selectedId) ?? null;

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 md:gap-4">
        <Kpi label="Entrées" value={totaux.entrees} tone="emerald" sign="+" loading={isLoading} />
        <Kpi label="Sorties" value={totaux.sorties} tone="red" sign="-" loading={isLoading} />
        <Kpi label="Mises de côté" value={totaux.misesDeCote} tone="violet" sign="" loading={isLoading} />
        <Kpi
          label="Net"
          value={totaux.net}
          tone={totaux.net >= 0 ? 'emerald' : 'red'}
          sign={totaux.net >= 0 ? '+' : '-'}
          loading={isLoading}
          highlight
        />
      </div>

      <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 overflow-hidden">
        <div className="p-5 border-b border-white/10">
          <h3 className="text-base md:text-lg font-semibold text-white">Entrées et sorties par catégorie</h3>
          <p className="text-white/40 text-xs mt-1">
            Clique une ligne pour voir ses transactions et en reclasser une. <span className="text-white/30">Net = entrées − sorties − mises de côté.</span>
          </p>
        </div>

        {isLoading ? (
          <div className="p-5 space-y-2">
            {[1, 2, 3, 4, 5].map((i) => (
              <div key={i} className="h-10 rounded bg-white/5 animate-pulse" />
            ))}
          </div>
        ) : rows.length === 0 ? (
          <p className="p-8 text-white/30 text-center text-sm">Aucun mouvement sur cette période</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[620px]">
              <thead>
                <tr className="border-b border-white/5">
                  <th className="text-left p-3 text-white/40 text-xs font-medium">Catégorie</th>
                  <th className="text-right p-3 text-white/40 text-xs font-medium">Entrées</th>
                  <th className="text-right p-3 text-white/40 text-xs font-medium">Sorties</th>
                  <th className="text-right p-3 text-white/40 text-xs font-medium" title="Argent mis de côté dans le mois (achat de titres). Sort du compte courant, donc compte dans le net.">
                    Mises de côté
                  </th>
                  <th className="text-right p-3 text-white/40 text-xs font-medium hidden sm:table-cell" title="Mouvements dont le montant ne se décide pas dans le mois : balayage du compte joint vers le livret, alimentation de la carte. Pour information, jamais soustraits.">
                    Hors bilan
                  </th>
                  <th className="text-right p-3 text-white/40 text-xs font-medium">Net</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr
                    key={r.categoryId}
                    onClick={() => setSelectedId(r.categoryId)}
                    title={`Voir les lignes de ${r.categoryName}`}
                    className="border-b border-white/5 last:border-0 hover:bg-white/5 transition-colors cursor-pointer"
                  >
                    <td className="p-3">
                      <div className="flex items-center gap-2">
                        <span aria-hidden="true">{r.categoryIcon}</span>
                        <span className="text-white text-sm">{r.categoryName}</span>
                      </div>
                    </td>
                    <Montant valeur={r.entrees} classe="text-emerald-400" signe="+" />
                    <Montant valeur={r.sorties} classe="text-red-400" signe="-" />
                    <Montant valeur={r.misesDeCote} classe="text-violet-400" signe="" />
                    <Montant valeur={r.horsBilan} classe="text-white/40" signe="" masquerPetitEcran />
                    <td className={`p-3 text-right text-sm font-semibold ${r.net >= 0 ? 'text-emerald-400' : 'text-red-400'}`}>
                      {r.net >= 0 ? '+' : '-'}
                      {formatCurrency(Math.abs(r.net))}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t border-white/10 bg-white/5">
                  <td className="p-3 text-white/60 text-xs font-medium uppercase tracking-wide">Total</td>
                  <td className="p-3 text-right text-emerald-400 text-sm font-semibold">+{formatCurrency(totaux.entrees)}</td>
                  <td className="p-3 text-right text-red-400 text-sm font-semibold">-{formatCurrency(totaux.sorties)}</td>
                  <td className="p-3 text-right text-violet-400 text-sm font-semibold">{formatCurrency(totaux.misesDeCote)}</td>
                  <td className="p-3 text-right text-white/40 text-sm font-semibold hidden sm:table-cell">{formatCurrency(totaux.horsBilan)}</td>
                  <td className={`p-3 text-right text-sm font-bold ${totaux.net >= 0 ? 'text-emerald-400' : 'text-red-400'}`}>
                    {totaux.net >= 0 ? '+' : '-'}
                    {formatCurrency(Math.abs(totaux.net))}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>
        )}
      </div>

      {selected && currentDashboard && (
        <CategoryFlowModal
          categoryId={selected.categoryId}
          categoryName={selected.categoryName}
          categoryIcon={selected.categoryIcon}
          entrees={selected.entrees}
          sorties={selected.sorties}
          net={selected.net}
          dashboardId={currentDashboard.id}
          period={period}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  );
};

interface MontantProps {
  valeur: number;
  classe: string;
  signe: string;
  masquerPetitEcran?: boolean;
}

const Montant = ({ valeur, classe, signe, masquerPetitEcran }: MontantProps) => (
  <td className={`p-3 text-right text-sm ${masquerPetitEcran ? 'hidden sm:table-cell' : ''}`}>
    {valeur !== 0 ? (
      <span className={classe}>{signe}{formatCurrency(valeur)}</span>
    ) : (
      <span className="text-white/20">—</span>
    )}
  </td>
);

interface KpiProps {
  label: string;
  value: number;
  tone: 'emerald' | 'red' | 'violet';
  sign: string;
  loading?: boolean;
  highlight?: boolean;
}

const Kpi = ({ label, value, tone, sign, loading, highlight }: KpiProps) => {
  const colors = {
    emerald: 'text-emerald-400',
    red: 'text-red-400',
    violet: 'text-violet-400',
  };
  return (
    <div className={`bg-white/5 backdrop-blur-xl rounded-2xl border p-4 md:p-5 ${highlight ? 'border-amber-500/30' : 'border-white/10'}`}>
      <p className="text-white/40 text-xs md:text-sm mb-1 truncate">{label}</p>
      {loading ? (
        <div className="h-8 w-24 rounded bg-white/5 animate-pulse" />
      ) : (
        <p className={`text-xl md:text-2xl lg:text-3xl font-bold ${colors[tone]}`} style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
          {sign}
          {formatCurrency(Math.abs(value))}
        </p>
      )}
    </div>
  );
};

export default DashboardFlows;
