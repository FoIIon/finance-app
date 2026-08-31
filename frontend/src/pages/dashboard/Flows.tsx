import { useMemo } from 'react';
import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../hooks/usePeriod';
import { useSummaryQuery } from '../../hooks/queries';
import { formatCurrency } from '../../utils/format';
import type { CategoryBreakdown } from '../../types/transaction';

/**
 * Une catégorie, ses deux sens et son net. Les autres onglets montrent les rentrées d'un côté et les
 * dépenses de l'autre, jamais les deux face à face, alors qu'une catégorie porte souvent les deux :
 * Enfants encaisse les allocations et paie la crèche, Santé paie le médecin et reçoit la mutuelle,
 * Sorties avance des places de foot et se fait rembourser.
 */
interface FlowRow {
  categoryId: number;
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  entrees: number;
  sorties: number;
  misesDeCote: number;
  net: number;
}

type FlowField = 'entrees' | 'sorties' | 'misesDeCote';

const fold = (rows: Map<number, FlowRow>, items: CategoryBreakdown[], champ: FlowField) => {
  for (const c of items) {
    const ligne = rows.get(c.categoryId) ?? {
      categoryId: c.categoryId,
      categoryName: c.categoryName,
      categoryIcon: c.categoryIcon,
      categoryColor: c.categoryColor,
      entrees: 0,
      sorties: 0,
      misesDeCote: 0,
      net: 0,
    };
    ligne[champ] += Number(c.amount);
    rows.set(c.categoryId, ligne);
  }
};

const DashboardFlows = () => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const { data: summary, isLoading } = useSummaryQuery(currentDashboard?.id, period);

  const rows = useMemo(() => {
    const map = new Map<number, FlowRow>();
    fold(map, summary?.incomeBreakdown ?? [], 'entrees');
    fold(map, summary?.categoryBreakdown ?? [], 'sorties');
    fold(map, summary?.savingsBreakdown ?? [], 'misesDeCote');
    return [...map.values()]
      .map((r) => ({ ...r, net: r.entrees - r.sorties - r.misesDeCote }))
      .sort((a, b) => Math.abs(b.net) - Math.abs(a.net));
  }, [summary]);

  const totaux = useMemo(
    () =>
      rows.reduce(
        (acc, r) => ({
          entrees: acc.entrees + r.entrees,
          sorties: acc.sorties + r.sorties,
          misesDeCote: acc.misesDeCote + r.misesDeCote,
          net: acc.net + r.net,
        }),
        { entrees: 0, sorties: 0, misesDeCote: 0, net: 0 }
      ),
    [rows]
  );

  const mixtes = rows.filter((r) => r.entrees > 0 && (r.sorties !== 0 || r.misesDeCote !== 0)).length;
  const legende =
    mixtes === 0
      ? 'Aucune catégorie ne porte les deux sens sur cette période.'
      : mixtes === 1
        ? '1 catégorie porte les deux sens sur cette période.'
        : mixtes + ' catégories portent les deux sens sur cette période.';

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
          <p className="text-white/40 text-xs mt-1">{legende}</p>
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
            <table className="w-full min-w-[520px]">
              <thead>
                <tr className="border-b border-white/5">
                  <th className="text-left p-3 text-white/40 text-xs font-medium">Catégorie</th>
                  <th className="text-right p-3 text-white/40 text-xs font-medium">Entrées</th>
                  <th className="text-right p-3 text-white/40 text-xs font-medium">Sorties</th>
                  <th className="text-right p-3 text-white/40 text-xs font-medium hidden sm:table-cell">Mises de côté</th>
                  <th className="text-right p-3 text-white/40 text-xs font-medium">Net</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.categoryId} className="border-b border-white/5 last:border-0 hover:bg-white/5 transition-colors">
                    <td className="p-3">
                      <div className="flex items-center gap-2">
                        <span aria-hidden="true">{r.categoryIcon}</span>
                        <span className="text-white text-sm">{r.categoryName}</span>
                      </div>
                    </td>
                    <td className="p-3 text-right text-sm">
                      {r.entrees > 0 ? (
                        <span className="text-emerald-400">+{formatCurrency(r.entrees)}</span>
                      ) : (
                        <span className="text-white/20">—</span>
                      )}
                    </td>
                    <td className="p-3 text-right text-sm">
                      {r.sorties !== 0 ? (
                        <span className="text-red-400">-{formatCurrency(r.sorties)}</span>
                      ) : (
                        <span className="text-white/20">—</span>
                      )}
                    </td>
                    <td className="p-3 text-right text-sm hidden sm:table-cell">
                      {r.misesDeCote !== 0 ? (
                        <span className="text-violet-400">{formatCurrency(r.misesDeCote)}</span>
                      ) : (
                        <span className="text-white/20">—</span>
                      )}
                    </td>
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
                  <td className="p-3 text-right text-violet-400 text-sm font-semibold hidden sm:table-cell">
                    {formatCurrency(totaux.misesDeCote)}
                  </td>
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
    </div>
  );
};

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
