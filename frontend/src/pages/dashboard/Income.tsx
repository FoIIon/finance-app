import { useState } from 'react';
import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../hooks/usePeriod';
import { useSummaryQuery } from '../../hooks/queries';
import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip } from 'recharts';
import { formatCurrency } from '../../utils/format';
import { CategoryDetailModal } from '../../components/dashboard/CategoryDetailModal';
import { TransactionType } from '../../types/transaction';

const DashboardIncome = () => {
  const { currentDashboard } = useDashboards();
  const { period } = usePeriod();
  const { data: summary, isLoading } = useSummaryQuery(currentDashboard?.id, period);
  const incomes = summary?.incomeBreakdown ?? [];
  const totalIncome = summary?.totalIncome ?? 0;
  // Le détail d'une rentrée se consulte comme celui d'une dépense : au clic sur la catégorie.
  const [selectedIdx, setSelectedIdx] = useState<number | null>(null);
  const selected = selectedIdx !== null ? incomes[selectedIdx] : null;

  return (
    <div className="space-y-5">
      {/* KPI */}
      <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
        <p className="text-white/40 text-xs font-medium uppercase tracking-wide">Total des rentrées</p>
        {isLoading ? (
          <div className="h-9 w-40 mt-1 rounded bg-white/5 animate-pulse" />
        ) : (
          <p className="text-3xl font-bold text-emerald-400 mt-1" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
            +{formatCurrency(totalIncome)}
          </p>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
          <h3 className="text-base md:text-lg font-semibold text-white mb-4">Répartition</h3>
          {isLoading ? (
            <div className="h-[300px] rounded bg-white/5 animate-pulse" />
          ) : incomes.length === 0 ? (
            <p className="text-white/30 text-center py-12 text-sm">Aucune rentrée sur cette période</p>
          ) : (
            <ResponsiveContainer width="100%" height={300}>
              <PieChart>
                <Pie
                  data={incomes}
                  dataKey="amount"
                  nameKey="categoryName"
                  cx="50%"
                  cy="50%"
                  outerRadius={100}
                  innerRadius={50}
                  strokeWidth={2}
                  stroke="#0a0a1a"
                  label={(entry: { name?: string }) => entry.name ?? ''}
                  labelLine={false}
                >
                  {incomes.map((entry, index) => (
                    <Cell key={index} fill={entry.categoryColor} />
                  ))}
                </Pie>
                <Tooltip
                  contentStyle={{ backgroundColor: '#1a1a3e', border: '1px solid rgba(255,255,255,0.1)', borderRadius: '12px', color: '#fff' }}
                  formatter={(value, name) => [formatCurrency(value as number), name]}
                />
              </PieChart>
            </ResponsiveContainer>
          )}
        </div>

        {/* Tableau détail */}
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 overflow-hidden">
          <div className="p-5 border-b border-white/10">
            <h3 className="text-base md:text-lg font-semibold text-white">Détail par catégorie</h3>
          </div>
          {isLoading ? (
            <div className="p-5 space-y-2">
              {[1, 2, 3].map(i => <div key={i} className="h-10 rounded bg-white/5 animate-pulse" />)}
            </div>
          ) : incomes.length === 0 ? (
            <p className="p-8 text-white/30 text-center text-sm">Aucune donnée</p>
          ) : (
            <table className="w-full">
              <thead>
                <tr className="border-b border-white/5">
                  <th className="text-left p-3 text-white/40 text-xs font-medium">Catégorie</th>
                  <th className="text-right p-3 text-white/40 text-xs font-medium">Montant</th>
                  <th className="text-right p-3 text-white/40 text-xs font-medium hidden sm:table-cell">% du total</th>
                </tr>
              </thead>
              <tbody>
                {incomes.map((c, i) => (
                  <tr
                    key={i}
                    onClick={() => setSelectedIdx(i)}
                    title={`Voir les lignes de ${c.categoryName}`}
                    className="border-b border-white/5 last:border-0 hover:bg-white/5 transition-colors cursor-pointer"
                  >
                    <td className="p-3">
                      <div className="flex items-center gap-2">
                        <span aria-hidden="true">{c.categoryIcon}</span>
                        <span className="text-white text-sm">{c.categoryName}</span>
                      </div>
                    </td>
                    <td className="p-3 text-right text-emerald-400 text-sm font-medium">
                      +{formatCurrency(c.amount)}
                    </td>
                    <td className="p-3 text-right text-white/50 text-sm hidden sm:table-cell">
                      {c.percentage}%
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>

      {selected && currentDashboard && (
        <CategoryDetailModal
          categoryId={selected.categoryId}
          categoryName={selected.categoryName}
          categoryIcon={selected.categoryIcon}
          categoryColor={selected.categoryColor}
          totalAmount={Number(selected.amount)}
          dashboardId={currentDashboard.id}
          period={period}
          type={TransactionType.Income}
          onClose={() => setSelectedIdx(null)}
        />
      )}
    </div>
  );
};

export default DashboardIncome;
