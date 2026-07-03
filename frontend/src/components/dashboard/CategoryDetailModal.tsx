import { useContext, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useQuery } from '@tanstack/react-query';
import { transactionsApi } from '../../api/transactions';
import { TransactionType } from '../../types/transaction';
import type { Period } from '../../utils/periods';
import { periodToRange } from '../../utils/periods';
import { formatCurrency } from '../../utils/format';
import { PeriodContext } from '../../context/PeriodContext';

// Helper local — on lit le filtre depuis le context sans dépendre du sous-fichier (évite la circularité)
const useBankFilter = () => useContext(PeriodContext).bankAccountFilter;

interface Props {
  categoryId: number;
  categoryName: string;
  categoryIcon: string;
  totalAmount: number;
  dashboardId: number;
  period: Period;
  onClose: () => void;
}

export const CategoryDetailModal = ({ categoryId, categoryName, categoryIcon, totalAmount, dashboardId, period, onClose }: Props) => {
  const { from, to } = periodToRange(period);
  const bankAccountId = useBankFilter();

  const { data: transactions, isLoading } = useQuery({
    queryKey: ['category-detail', dashboardId, categoryId, period.key, bankAccountId],
    queryFn: async () => {
      const res = await transactionsApi.getAll({
        dashboardId,
        categoryId,
        from,
        to,
        type: TransactionType.Expense,
        bankAccountId,
        sortBy: 'date',
        sortDesc: true,
      });
      return res.data;
    },
  });

  useEffect(() => {
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onEsc);
    return () => document.removeEventListener('keydown', onEsc);
  }, [onClose]);

  const modal = (
    <div
      role="dialog"
      aria-modal="true"
      className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-end md:items-center justify-center"
      onClick={onClose}
    >
      <div
        className="bg-[#1a1a3e] rounded-t-2xl md:rounded-2xl border border-white/10 p-6 md:p-8 w-full md:max-w-2xl max-h-[85vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between mb-4">
          <div>
            <h3 className="text-xl font-bold text-white flex items-center gap-2">
              <span aria-hidden="true">{categoryIcon}</span> {categoryName}
            </h3>
            <p className="text-white/50 text-sm mt-1">
              {period.label} · <span className="text-red-400 font-semibold">{formatCurrency(totalAmount)}</span>
            </p>
          </div>
          <button
            onClick={onClose}
            aria-label="Fermer"
            className="text-white/40 hover:text-white text-2xl leading-none px-2"
          >
            ×
          </button>
        </div>

        <div className="flex-1 overflow-y-auto -mx-2 px-2">
          {isLoading ? (
            <div className="space-y-2">
              {[1, 2, 3, 4].map(i => <div key={i} className="h-12 rounded bg-white/5 animate-pulse" />)}
            </div>
          ) : !transactions || transactions.length === 0 ? (
            <p className="text-white/30 text-center py-8 text-sm">Aucune transaction</p>
          ) : (
            <ul className="divide-y divide-white/5">
              {transactions.map((t) => (
                <li key={t.id} className="py-3 flex items-start justify-between gap-3">
                  <div className="min-w-0 flex-1">
                    <p className="text-white text-sm truncate">
                      {t.description || <em className="text-white/30">(sans libellé)</em>}
                    </p>
                    {t.counterpartyName && (
                      <p className="text-white/50 text-xs truncate mt-0.5">
                        {t.type === TransactionType.Income ? '↩ De ' : '↪ Vers '}{t.counterpartyName}
                      </p>
                    )}
                    <p className="text-white/40 text-xs mt-0.5">{new Date(t.date).toLocaleDateString('fr-FR')}</p>
                  </div>
                  <span className={`text-sm font-semibold flex-shrink-0 ${t.type === TransactionType.Income ? 'text-emerald-400' : 'text-red-400'}`}>
                    {t.type === TransactionType.Income ? '+' : '-'}{formatCurrency(t.amount)}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>

        {transactions && transactions.length > 0 && (
          <div className="mt-4 pt-3 border-t border-white/10 text-white/40 text-xs text-right">
            {transactions.length} transaction{transactions.length > 1 ? 's' : ''}
          </div>
        )}
      </div>
    </div>
  );

  return createPortal(modal, document.body);
};
