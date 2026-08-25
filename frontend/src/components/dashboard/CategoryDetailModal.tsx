import { useContext, useEffect, useMemo } from 'react';
import { createPortal } from 'react-dom';
import { useQuery } from '@tanstack/react-query';
import { BarChart, Bar, XAxis, YAxis, Tooltip, CartesianGrid, ResponsiveContainer, ReferenceLine } from 'recharts';
import { transactionsApi } from '../../api/transactions';
import { TransactionType } from '../../types/transaction';
import type { Period } from '../../utils/periods';
import { periodToRange } from '../../utils/periods';
import { formatCurrency } from '../../utils/format';
import { PeriodContext } from '../../context/period-context';

// Helper local — on lit le filtre depuis le context sans dépendre du sous-fichier (évite la circularité)
const useBankFilter = () => useContext(PeriodContext).bankAccountFilter;

interface Props {
  categoryId: number;
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  totalAmount: number;
  dashboardId: number;
  period: Period;
  onClose: () => void;
}

export const CategoryDetailModal = ({ categoryId, categoryName, categoryIcon, categoryColor, totalAmount, dashboardId, period, onClose }: Props) => {
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

  const { data: history, isLoading: historyLoading, isError: historyError } = useQuery({
    queryKey: ['category-history', dashboardId, categoryId],
    queryFn: async () => {
      const res = await transactionsApi.getCategoryHistory(dashboardId, categoryId, 12);
      return res.data;
    },
  });

  // Moyenne des 6 mois révolus (hors mois courant) sur le flux courant (hors exceptionnel)
  const { avg6, current, deltaPct, hasExceptional } = useMemo(() => {
    const h = history ?? [];
    const revolus = h.slice(-7, -1); // 6 mois précédant le mois courant
    const avg = revolus.length ? revolus.reduce((s, m) => s + m.currentTotal, 0) / revolus.length : 0;
    const cur = h.length ? h[h.length - 1] : undefined;
    const curVal = cur?.currentTotal ?? 0;
    return {
      avg6: avg,
      current: cur,
      deltaPct: avg > 0 ? ((curVal - avg) / avg) * 100 : 0,
      hasExceptional: h.some(m => m.exceptionalTotal > 0),
    };
  }, [history]);

  useEffect(() => {
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onEsc);
    return () => document.removeEventListener('keydown', onEsc);
  }, [onClose]);

  const showChart = !historyError && history && history.length > 0;
  const above = current ? (current.currentTotal > avg6) : false;

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

        {/* Évolution 12 mois — barres empilées courant/exceptionnel + moyenne 6 mois glissante */}
        {historyLoading ? (
          <div className="h-[200px] rounded bg-white/5 animate-pulse mb-4" />
        ) : showChart ? (
          <div className="mb-4">
            <ResponsiveContainer width="100%" height={190}>
              <BarChart data={history} margin={{ top: 12, right: 8, left: -8, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.05)" vertical={false} />
                <XAxis
                  dataKey="label"
                  stroke="rgba(255,255,255,0.3)"
                  fontSize={11}
                  tickFormatter={(v: string) => v.split(' ')[0]}
                />
                <YAxis stroke="rgba(255,255,255,0.3)" fontSize={11} width={48} />
                <Tooltip
                  contentStyle={{ backgroundColor: '#1a1a3e', border: '1px solid rgba(255,255,255,0.1)', borderRadius: '12px', color: '#fff' }}
                  labelStyle={{ color: 'rgba(255,255,255,0.6)' }}
                  cursor={{ fill: 'rgba(255,255,255,0.04)' }}
                  formatter={(value, name) => [formatCurrency(value as number), name as string]}
                />
                <Bar dataKey="currentTotal" stackId="a" fill={categoryColor} name="Courant" radius={hasExceptional ? [0, 0, 0, 0] : [3, 3, 0, 0]} />
                <Bar dataKey="exceptionalTotal" stackId="a" fill={categoryColor} fillOpacity={0.45} name="Exceptionnel" radius={[3, 3, 0, 0]} />
                {avg6 > 0 && (
                  <ReferenceLine
                    y={avg6}
                    stroke="rgba(255,255,255,0.45)"
                    strokeDasharray="4 4"
                    label={{ value: 'moy. 6 mois', position: 'insideTopRight', fill: 'rgba(255,255,255,0.5)', fontSize: 10 }}
                  />
                )}
              </BarChart>
            </ResponsiveContainer>
            {current && avg6 > 0 && (
              <p className="text-xs text-white/50 text-center mt-1">
                {current.label} : <span className="text-white/80 font-medium">{formatCurrency(current.currentTotal)}</span>
                {' · moy. 6 mois : '}<span className="text-white/80 font-medium">{formatCurrency(avg6)}</span>
                {' · '}
                <span className={above ? 'text-red-400 font-semibold' : 'text-emerald-400 font-semibold'}>
                  {deltaPct >= 0 ? '+' : ''}{deltaPct.toFixed(0)} %
                </span>
              </p>
            )}
          </div>
        ) : null}

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
                      {t.isExceptional && <span className="text-amber-400 mr-1" title="Dépense exceptionnelle" aria-label="Dépense exceptionnelle">⚡</span>}
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
