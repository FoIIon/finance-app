import { useQuery } from '@tanstack/react-query';
import { transactionsApi } from '../api/transactions';
import { useDashboards } from '../hooks/useDashboards';
import { TransactionType } from '../types/transaction';
import type { Transaction } from '../types/transaction';
import { formatCurrency } from '../utils/format';

interface MonthGroup {
  key: string;
  label: string;
  total: number;
  transactions: Transaction[];
}

const monthLabel = (key: string) => {
  const [year, month] = key.split('-').map(Number);
  const label = new Date(year, month - 1, 1).toLocaleDateString('fr-FR', { month: 'long', year: 'numeric' });
  return label.charAt(0).toUpperCase() + label.slice(1);
};

const ExceptionalExpenses = () => {
  const { currentDashboard } = useDashboards();
  const dashboardId = currentDashboard?.id;

  const { data: transactions, isLoading } = useQuery({
    queryKey: ['exceptional-expenses', dashboardId],
    enabled: !!dashboardId,
    queryFn: async () => {
      const res = await transactionsApi.getAll({
        dashboardId,
        isExceptional: true,
        type: TransactionType.Expense,
        sortBy: 'date',
        sortDesc: true,
      });
      return res.data;
    },
  });

  const groups: MonthGroup[] = [];
  let grandTotal = 0;
  if (transactions) {
    const byMonth = new Map<string, Transaction[]>();
    for (const t of transactions) {
      const d = new Date(t.date);
      const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
      if (!byMonth.has(key)) byMonth.set(key, []);
      byMonth.get(key)!.push(t);
      grandTotal += Number(t.amount);
    }
    for (const key of Array.from(byMonth.keys()).sort().reverse()) {
      const txns = byMonth.get(key)!;
      groups.push({
        key,
        label: monthLabel(key),
        total: txns.reduce((s, t) => s + Number(t.amount), 0),
        transactions: txns,
      });
    }
  }

  return (
    <div className="space-y-6 animate-[fadeIn_0.5s_ease-out]">
      <div className="flex items-center justify-between flex-wrap gap-3">
        <h2 className="text-3xl font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
          Grosses dépenses {currentDashboard ? `— ${currentDashboard.name}` : ''}
        </h2>
      </div>

      <p className="text-white/50 text-sm">
        Dépenses marquées exceptionnelles (⚡), exclues des totaux mensuels par défaut. Vue transverse tous mois confondus.
      </p>

      {/* Total général */}
      <div className="bg-white/5 rounded-2xl border border-white/10 p-5 flex items-center justify-between">
        <span className="text-white/60 text-sm">Total des dépenses exceptionnelles</span>
        <span className="text-2xl font-bold text-amber-400">{formatCurrency(grandTotal)}</span>
      </div>

      {isLoading ? (
        <div className="p-8 text-center text-white/40">Chargement...</div>
      ) : groups.length === 0 ? (
        <div className="bg-white/5 rounded-2xl border border-white/10 p-8 text-center text-white/30">
          Aucune dépense exceptionnelle. Marquez-en depuis la page Transactions avec le bouton ⚡.
        </div>
      ) : (
        <div className="space-y-5">
          {groups.map((g) => (
            <div key={g.key} className="bg-white/5 rounded-2xl border border-white/10 overflow-hidden">
              <div className="flex items-center justify-between px-5 py-3 border-b border-white/10">
                <h3 className="text-white font-semibold">{g.label}</h3>
                <span className="text-amber-400 font-semibold">{formatCurrency(g.total)}</span>
              </div>
              <ul className="divide-y divide-white/5">
                {g.transactions.map((t) => (
                  <li key={t.id} className="px-5 py-3 flex items-start justify-between gap-3">
                    <div className="min-w-0 flex-1">
                      <p className="text-white text-sm truncate">
                        {t.description || <em className="text-white/30">(sans libellé)</em>}
                      </p>
                      <p className="text-white/40 text-xs mt-0.5">
                        {new Date(t.date).toLocaleDateString('fr-FR')} · {t.categoryIcon} {t.categoryName}
                      </p>
                    </div>
                    <span className="text-red-400 text-sm font-semibold flex-shrink-0">-{formatCurrency(t.amount)}</span>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default ExceptionalExpenses;
