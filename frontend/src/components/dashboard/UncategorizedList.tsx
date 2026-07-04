import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useDashboards } from '../../hooks/useDashboards';
import { useUncategorizedQuery } from '../../hooks/queries';
import { transactionsApi } from '../../api/transactions';
import { categoriesApi } from '../../api/categories';
import type { Category } from '../../types/category';
import { TransactionType } from '../../types/transaction';
import { formatCurrency } from '../../utils/format';
import { useEffect } from 'react';

export const UncategorizedList = () => {
  const { currentDashboard } = useDashboards();
  const { data, isLoading } = useUncategorizedQuery(currentDashboard?.id, 50);
  const [categories, setCategories] = useState<Category[]>([]);
  const [savingId, setSavingId] = useState<number | null>(null);
  const qc = useQueryClient();

  useEffect(() => {
    categoriesApi.getAll().then(r => setCategories(r.data));
  }, []);

  const handleAssign = async (txnId: number, categoryId: number) => {
    const txn = data?.find(t => t.id === txnId);
    if (!txn) return;
    setSavingId(txnId);
    try {
      await transactionsApi.update(txnId, {
        amount: txn.amount,
        description: txn.description,
        date: txn.date,
        type: txn.type,
        categoryId,
        accountId: txn.accountId,
      });
      qc.invalidateQueries({ queryKey: ['uncategorized'] });
      qc.invalidateQueries({ queryKey: ['summary'] });
    } finally {
      setSavingId(null);
    }
  };

  return (
    <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-base md:text-lg font-semibold text-white">Transactions à catégoriser</h3>
        <span className="text-xs px-2 py-0.5 rounded-full bg-amber-500/20 text-amber-300 border border-amber-500/30">
          {data?.length ?? 0}
        </span>
      </div>
      {isLoading ? (
        <div className="space-y-2">
          {[1, 2, 3].map(i => <div key={i} className="h-12 rounded bg-white/5 animate-pulse" />)}
        </div>
      ) : !data || data.length === 0 ? (
        <p className="text-white/30 text-center py-6 text-sm">
          ✓ Tout est catégorisé
        </p>
      ) : (
        <div className="space-y-2 max-h-[600px] overflow-y-auto">
          {data.map((t) => (
            <div key={t.id} className="flex items-center justify-between gap-3 py-2 border-b border-white/5 last:border-0">
              <div className="min-w-0 flex-1">
                <p className="text-white text-sm font-medium truncate">{t.description || <em className="text-white/30">(sans libellé)</em>}</p>
                <p className="text-white/40 text-xs truncate">
                  {t.counterpartyName && <>{t.type === TransactionType.Income ? '↩ ' : '↪ '}{t.counterpartyName} · </>}
                  {new Date(t.date).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short' })}
                </p>
              </div>
              <span className={`text-sm font-semibold whitespace-nowrap ${t.type === TransactionType.Income ? 'text-emerald-400' : 'text-red-400'}`}>
                {t.type === TransactionType.Income ? '+' : '-'}{formatCurrency(t.amount)}
              </span>
              <select
                disabled={savingId === t.id}
                onChange={(e) => e.target.value && handleAssign(t.id, Number(e.target.value))}
                className="text-xs px-2 py-1 rounded-lg bg-white/10 border border-white/10 text-white focus:outline-none focus:border-amber-500/50 disabled:opacity-50"
                defaultValue=""
              >
                <option value="">→ catégorie</option>
                {categories.filter(c => c.id !== 10).map(c => (
                  <option key={c.id} value={c.id}>{c.icon} {c.name}</option>
                ))}
              </select>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
