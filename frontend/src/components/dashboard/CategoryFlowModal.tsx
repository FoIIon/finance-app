import { useContext, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { transactionsApi } from '../../api/transactions';
import { TransactionType } from '../../types/transaction';
import type { Period } from '../../utils/periods';
import { periodToRange } from '../../utils/periods';
import { formatCurrency } from '../../utils/format';
import { useCategoriesQuery } from '../../hooks/queries';
import { useToast } from '../../hooks/useToast';
import { PeriodContext } from '../../context/period-context';
import { CategoryFlowChart } from './CategoryFlowChart';

interface Props {
  categoryId: number;
  categoryName: string;
  categoryIcon: string;
  entrees: number;
  sorties: number;
  net: number;
  dashboardId: number;
  period: Period;
  onClose: () => void;
}

/**
 * Toutes les lignes d'une catégorie sur la période, les deux sens mêlés, et le moyen de reclasser
 * une ligne sur place. Une catégorie changée ici est marquée comme choix humain : le tri suivant
 * relit ces corrections pour comprendre quelle règle manque (voir ManualCategoryTrace côté API).
 */
export const CategoryFlowModal = ({
  categoryId,
  categoryName,
  categoryIcon,
  entrees,
  sorties,
  net,
  dashboardId,
  period,
  onClose,
}: Props) => {
  const { from, to } = periodToRange(period);
  // Le tableau des flux est produit par un résumé filtré par compte et par exceptionnel. Le graphe et
  // la liste doivent lire les mêmes filtres, sinon le détail contredit la ligne qui l'a ouvert.
  const { bankAccountFilter, includeExceptional } = useContext(PeriodContext);
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const { data: categories } = useCategoriesQuery();
  const [enCours, setEnCours] = useState<number | null>(null);

  const { data: transactions, isLoading, refetch } = useQuery({
    queryKey: ['category-flow', dashboardId, categoryId, period.key, bankAccountFilter],
    queryFn: async () => {
      const res = await transactionsApi.getAll({
        dashboardId,
        categoryId,
        from,
        to,
        bankAccountId: bankAccountFilter,
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

  const reclasser = async (transactionId: number, nouvelleCategorie: number) => {
    setEnCours(transactionId);
    try {
      const maj = await transactionsApi.setCategory(transactionId, nouvelleCategorie);
      showToast(`Reclassée en ${maj.data.categoryName}`, 'success');
      await refetch();
      queryClient.invalidateQueries({ queryKey: ['summary'] });
      queryClient.invalidateQueries({ queryKey: ['monthly-report'] });
      queryClient.invalidateQueries({ queryKey: ['category-detail'] });
      queryClient.invalidateQueries({ queryKey: ['category-history'] });
      queryClient.invalidateQueries({ queryKey: ['category-flow-history'] });
    } catch {
      showToast('Impossible de changer la catégorie', 'error');
    } finally {
      setEnCours(null);
    }
  };

  const modal = (
    <div
      role="dialog"
      aria-modal="true"
      className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-end md:items-center justify-center"
      onClick={onClose}
    >
      <div
        className="bg-[#1a1a3e] rounded-t-2xl md:rounded-2xl border border-white/10 p-6 md:p-8 w-full md:max-w-3xl max-h-[85vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between mb-4">
          <div>
            <h3 className="text-xl font-bold text-white flex items-center gap-2">
              <span aria-hidden="true">{categoryIcon}</span> {categoryName}
            </h3>
            <p className="text-white/50 text-sm mt-1">
              {period.label} ·{' '}
              {entrees > 0 && <span className="text-emerald-400 font-semibold">+{formatCurrency(entrees)}</span>}
              {entrees > 0 && sorties !== 0 && <span className="text-white/30"> · </span>}
              {sorties !== 0 && <span className="text-red-400 font-semibold">-{formatCurrency(sorties)}</span>}
              <span className="text-white/30"> · net </span>
              <span className={net >= 0 ? 'text-emerald-400 font-semibold' : 'text-red-400 font-semibold'}>
                {net >= 0 ? '+' : '-'}{formatCurrency(Math.abs(net))}
              </span>
            </p>
          </div>
          <button onClick={onClose} aria-label="Fermer" className="text-white/40 hover:text-white text-2xl leading-none px-2">
            ×
          </button>
        </div>

        <CategoryFlowChart
          categoryId={categoryId}
          dashboardId={dashboardId}
          bankAccountId={bankAccountFilter}
          includeExceptional={includeExceptional}
        />

        <div className="overflow-y-auto flex-1 -mx-2 px-2">
          {isLoading ? (
            <div className="space-y-2">
              {[1, 2, 3, 4].map((i) => <div key={i} className="h-12 rounded bg-white/5 animate-pulse" />)}
            </div>
          ) : !transactions || transactions.length === 0 ? (
            <p className="text-white/30 text-center py-10 text-sm">Aucune ligne sur cette période</p>
          ) : (
            <ul className="divide-y divide-white/5">
              {transactions.map((t) => (
                <li key={t.id} className="py-2.5 flex items-center gap-3">
                  <div className="flex-1 min-w-0">
                    <p className="text-white text-sm truncate">
                      {t.description || <em className="text-white/30">(sans libellé)</em>}
                      {t.categorySetManuallyAt && (
                        <span
                          className="ml-2 text-amber-400/80 text-xs"
                          title={`Catégorie corrigée à la main${t.categoryBeforeManualName ? ` (venait de ${t.categoryBeforeManualName})` : ''}`}
                        >
                          ✎
                        </span>
                      )}
                    </p>
                    <p className="text-white/40 text-xs truncate">
                      {new Date(t.date).toLocaleDateString('fr-FR')}
                      {t.counterpartyName && <> · {t.type === TransactionType.Income ? '↩ De ' : '↪ Vers '}{t.counterpartyName}</>}
                    </p>
                  </div>

                  <span className={`text-sm font-semibold flex-shrink-0 ${t.type === TransactionType.Income ? 'text-emerald-400' : 'text-red-400'}`}>
                    {t.type === TransactionType.Income ? '+' : '-'}{formatCurrency(t.amount)}
                  </span>

                  <select
                    value={t.categoryId}
                    disabled={enCours === t.id}
                    onChange={(e) => reclasser(t.id, Number(e.target.value))}
                    aria-label={`Catégorie de ${t.description || 'la transaction'}`}
                    className="bg-white/5 border border-white/10 rounded-lg px-2 py-1 text-white text-xs max-w-[9rem] focus:outline-none focus:border-amber-500/50 disabled:opacity-40"
                  >
                    {(categories ?? []).map((c) => (
                      <option key={c.id} value={c.id} className="bg-[#1a1a3e]">
                        {c.icon} {c.name}
                      </option>
                    ))}
                  </select>
                </li>
              ))}
            </ul>
          )}
        </div>

        <p className="text-white/30 text-xs mt-4">
          Une catégorie changée ici est marquée comme choix humain. Yen les relit au tri suivant pour comprendre quelle règle manque.
        </p>
      </div>
    </div>
  );

  return createPortal(modal, document.body);
};
