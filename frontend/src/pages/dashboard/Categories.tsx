import { useDashboards } from '../../hooks/useDashboards';
import { usePeriod } from '../../hooks/usePeriod';
import { useSummaryQuery } from '../../hooks/queries';
import { CategoryBreakdownBars } from '../../components/dashboard/CategoryBreakdown';
import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip } from 'recharts';
import { formatCurrency } from '../../utils/format';

const DashboardCategories = () => {
  const { currentDashboard } = useDashboards();
  const { period, includeExceptional } = usePeriod();
  const { data: summary, isLoading } = useSummaryQuery(currentDashboard?.id, period);
  const categories = summary?.categoryBreakdown ?? [];
  const totalExpenses = summary?.totalExpenses ?? 0;
  const exceptionalExpenses = summary?.exceptionalExpenses ?? 0;

  return (
    <div className="space-y-5">
      {/* KPI */}
      <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
        <p className="text-white/40 text-xs font-medium uppercase tracking-wide">Total des dépenses</p>
        {isLoading ? (
          <div className="h-9 w-40 mt-1 rounded bg-white/5 animate-pulse" />
        ) : (
          <div className="flex items-baseline gap-3 mt-1">
            <p className="text-3xl font-bold text-red-400" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
              −{formatCurrency(totalExpenses)}
            </p>
            {exceptionalExpenses > 0 && (
              <p className="text-white/40 text-sm">
                {includeExceptional
                  ? `dont ${formatCurrency(exceptionalExpenses)} exceptionnelles`
                  : `+ ${formatCurrency(exceptionalExpenses)} exceptionnelles (exclues)`}
              </p>
            )}
          </div>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
          <h3 className="text-base md:text-lg font-semibold text-white mb-4">Répartition</h3>
          {isLoading ? (
            <div className="h-[300px] rounded bg-white/5 animate-pulse" />
          ) : categories.length === 0 ? (
            <p className="text-white/30 text-center py-12 text-sm">Aucune dépense sur cette période</p>
          ) : (
            <ResponsiveContainer width="100%" height={300}>
              <PieChart>
                <Pie
                  data={categories}
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
                  {categories.map((entry, index) => (
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

        <CategoryBreakdownBars topN={20} />
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
        ) : categories.length === 0 ? (
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
              {categories.map((c, i) => (
                <tr key={i} className="border-b border-white/5 last:border-0 hover:bg-white/5 transition-colors">
                  <td className="p-3">
                    <div className="flex items-center gap-2">
                      <span aria-hidden="true">{c.categoryIcon}</span>
                      <span className="text-white text-sm">{c.categoryName}</span>
                    </div>
                  </td>
                  <td className="p-3 text-right text-white text-sm font-medium">
                    {formatCurrency(c.amount)}
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
  );
};

export default DashboardCategories;
