import { useEffect } from 'react';
import { useTransactions } from '../hooks/useTransactions';
import { useDashboards } from '../hooks/useDashboards';
import { TransactionType } from '../types/transaction';
import { formatCurrency } from '../utils/format';
import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip, LineChart, Line, XAxis, YAxis, CartesianGrid } from 'recharts';

const Dashboard = () => {
  const { transactions, summary, fetchSummary, loading } = useTransactions();
  const { currentDashboard } = useDashboards();

  useEffect(() => {
    fetchSummary();
  }, [fetchSummary]);

  const recentTransactions = transactions.slice(0, 5);

  if (loading && !summary) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-white/40">Chargement...</div>
      </div>
    );
  }

  return (
    <div className="space-y-8 animate-[fadeIn_0.5s_ease-out]">
      <h2 className="text-3xl font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
        {currentDashboard?.name ?? 'Tableau de bord'}
      </h2>

      {/* Cartes résumé */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-6 hover:border-amber-500/30 transition-all duration-300">
          <p className="text-white/40 text-sm mb-1">Solde total</p>
          <p className={`text-3xl font-bold ${(summary?.balance ?? 0) >= 0 ? 'text-emerald-400' : 'text-red-400'}`}>
            {formatCurrency(summary?.balance ?? 0)}
          </p>
        </div>
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-6 hover:border-emerald-500/30 transition-all duration-300">
          <p className="text-white/40 text-sm mb-1">Revenus</p>
          <p className="text-3xl font-bold text-emerald-400">
            {formatCurrency(summary?.totalIncome ?? 0)}
          </p>
        </div>
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-6 hover:border-red-500/30 transition-all duration-300">
          <p className="text-white/40 text-sm mb-1">Dépenses</p>
          <p className="text-3xl font-bold text-red-400">
            {formatCurrency(summary?.totalExpenses ?? 0)}
          </p>
        </div>
      </div>

      {/* Graphiques */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Camembert dépenses par catégorie */}
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-6">
          <h3 className="text-lg font-semibold text-white mb-4">Dépenses par catégorie</h3>
          {summary?.categoryBreakdown && summary.categoryBreakdown.length > 0 ? (
            <ResponsiveContainer width="100%" height={300}>
              <PieChart>
                <Pie
                  data={summary.categoryBreakdown}
                  dataKey="amount"
                  nameKey="categoryName"
                  cx="50%"
                  cy="50%"
                  outerRadius={100}
                  strokeWidth={2}
                  stroke="#0a0a1a"
                >
                  {summary.categoryBreakdown.map((entry, index) => (
                    <Cell key={index} fill={entry.categoryColor} />
                  ))}
                </Pie>
                <Tooltip
                  contentStyle={{ backgroundColor: '#1a1a3e', border: '1px solid rgba(255,255,255,0.1)', borderRadius: '12px', color: '#fff' }}
                  itemStyle={{ color: '#fff' }}
                  formatter={(value) => formatCurrency(value as number)}
                />
              </PieChart>
            </ResponsiveContainer>
          ) : (
            <div className="flex items-center justify-center h-[300px] text-white/30">
              Aucune dépense enregistrée
            </div>
          )}
        </div>

        {/* Courbe évolution mensuelle */}
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-6">
          <h3 className="text-lg font-semibold text-white mb-4">Évolution sur 6 mois</h3>
          {summary?.monthlyBalance && summary.monthlyBalance.length > 0 ? (
            <ResponsiveContainer width="100%" height={300}>
              <LineChart data={summary.monthlyBalance}>
                <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.05)" />
                <XAxis dataKey="month" stroke="rgba(255,255,255,0.3)" fontSize={12} />
                <YAxis stroke="rgba(255,255,255,0.3)" fontSize={12} />
                <Tooltip
                  contentStyle={{ backgroundColor: '#1a1a3e', border: '1px solid rgba(255,255,255,0.1)', borderRadius: '12px', color: '#fff' }}
                  itemStyle={{ color: '#fff' }}
                  formatter={(value) => formatCurrency(value as number)}
                />
                <Line type="monotone" dataKey="income" stroke="#34d399" strokeWidth={2} dot={{ fill: '#34d399' }} name="Revenus" />
                <Line type="monotone" dataKey="expenses" stroke="#f87171" strokeWidth={2} dot={{ fill: '#f87171' }} name="Dépenses" />
              </LineChart>
            </ResponsiveContainer>
          ) : (
            <div className="flex items-center justify-center h-[300px] text-white/30">
              Aucune donnée disponible
            </div>
          )}
        </div>
      </div>

      {/* Dernières transactions */}
      <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-6">
        <h3 className="text-lg font-semibold text-white mb-4">Dernières transactions</h3>
        {recentTransactions.length > 0 ? (
          <div className="space-y-3">
            {recentTransactions.map((t) => (
              <div key={t.id} className="flex items-center justify-between py-3 border-b border-white/5 last:border-0">
                <div className="flex items-center gap-3">
                  <span className="text-2xl">{t.categoryIcon}</span>
                  <div>
                    <p className="text-white font-medium">{t.description}</p>
                    <p className="text-white/40 text-sm">{t.categoryName} · {t.accountName} · {new Date(t.date).toLocaleDateString('fr-FR')}</p>
                  </div>
                </div>
                <span className={`font-semibold ${t.type === TransactionType.Income ? 'text-emerald-400' : 'text-red-400'}`}>
                  {t.type === TransactionType.Income ? '+' : '-'}{formatCurrency(t.amount)}
                </span>
              </div>
            ))}
          </div>
        ) : (
          <p className="text-white/30 text-center py-8">Aucune transaction enregistrée</p>
        )}
      </div>
    </div>
  );
};

export default Dashboard;
