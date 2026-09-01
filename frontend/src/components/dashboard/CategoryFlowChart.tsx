import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  ComposedChart, Bar, Line, XAxis, YAxis, Tooltip, CartesianGrid, ResponsiveContainer, ReferenceLine, Legend,
} from 'recharts';
import { transactionsApi } from '../../api/transactions';
import { formatCurrency } from '../../utils/format';

interface Props {
  categoryId: number;
  dashboardId: number;
  bankAccountId?: number;
  includeExceptional: boolean;
}

/**
 * L'évolution d'une catégorie dans les deux sens, sous la ligne qu'on vient de cliquer.
 *
 * Il branche category-flow-history et pas category-history : le second n'agrège que les dépenses
 * brutes, il afficherait donc un autre chiffre que la ligne du tableau dès qu'un remboursement est
 * dans le mois. Il passe aussi le filtre par compte et le filtre exceptionnel, pour la même raison.
 *
 * Le comparatif N-1 n'est dessiné que quand l'API le déclare comparable. Les comptes bancaires ont été
 * connectés le 30/01/2026 et la timeline Trade Republic remonte à novembre 2023 : sur les mois d'avant,
 * une catégorie ne porte que ses paiements par carte, jamais ses prélèvements ni ses courses. Le
 * repère vertical « banque » marque cette frontière pour qu'une marche dans la courbe ne se lise pas
 * comme une baisse de dépenses.
 */
export const CategoryFlowChart = ({ categoryId, dashboardId, bankAccountId, includeExceptional }: Props) => {
  const [months, setMonths] = useState<6 | 12>(6);

  const { data, isLoading, isError } = useQuery({
    queryKey: ['category-flow-history', dashboardId, categoryId, months, bankAccountId, includeExceptional],
    queryFn: async () => {
      const res = await transactionsApi.getCategoryFlowHistory(
        dashboardId, categoryId, months, bankAccountId, includeExceptional
      );
      return res.data;
    },
  });

  const moyennes = useMemo(() => {
    // Mois révolus seulement : le mois courant est incomplet et tirerait toutes les moyennes vers le bas.
    const revolus = (data?.months ?? []).slice(0, -1);
    if (!revolus.length) return null;
    const somme = (f: (m: (typeof revolus)[number]) => number) => revolus.reduce((s, m) => s + f(m), 0);
    return {
      n: revolus.length,
      income: somme((m) => m.income) / revolus.length,
      expenses: somme((m) => m.expenses) / revolus.length,
      net: somme((m) => m.net) / revolus.length,
    };
  }, [data]);

  if (isError) return null;
  if (isLoading || !data) return <div className="h-[210px] rounded bg-white/5 animate-pulse mb-4" />;

  const transfert = data.isTransferCategory;
  const moisFrontiere = data.firstFullBankMonth?.slice(0, 7);
  // Un repère sur la toute première barre n'apprend rien : la frontière est alors hors fenêtre.
  const frontiere = data.months.find((m, i) => i > 0 && m.month === moisFrontiere);
  const comparatif = data.previousYearAvailable;

  const dateFr = (iso: string | null) =>
    iso ? new Date(iso).toLocaleDateString('fr-FR', { month: 'long', year: 'numeric' }) : null;

  return (
    <div className="mb-4">
      <div className="flex items-center justify-between mb-1">
        <p className="text-white/40 text-xs">Évolution</p>
        <div className="flex gap-1" role="group" aria-label="Fenêtre de l'évolution">
          {([6, 12] as const).map((n) => (
            <button
              key={n}
              onClick={() => setMonths(n)}
              aria-pressed={months === n}
              className={`px-2 py-0.5 rounded-md text-xs transition-colors ${
                months === n ? 'bg-amber-500/20 text-amber-300' : 'text-white/40 hover:text-white/70'
              }`}
            >
              {n} mois
            </button>
          ))}
        </div>
      </div>

      <ResponsiveContainer width="100%" height={210}>
        <ComposedChart data={data.months} margin={{ top: 12, right: 8, left: -8, bottom: 0 }}>
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
          <Legend wrapperStyle={{ fontSize: 11, color: 'rgba(255,255,255,0.5)' }} iconSize={8} />
          <ReferenceLine y={0} stroke="rgba(255,255,255,0.2)" />

          {transfert ? (
            <Bar dataKey="savings" name="Mises de côté" fill="#a78bfa" radius={[3, 3, 0, 0]} />
          ) : (
            <>
              <Bar dataKey="income" name="Entrées" fill="#34d399" radius={[3, 3, 0, 0]} />
              <Bar dataKey="expenses" name="Sorties" fill="#f87171" radius={[3, 3, 0, 0]} />
            </>
          )}

          <Line
            type="monotone"
            dataKey="net"
            name="Net"
            stroke="#fbbf24"
            strokeWidth={2}
            dot={{ r: 2, fill: '#fbbf24' }}
            activeDot={{ r: 4 }}
          />

          {comparatif && !transfert && (
            <>
              <Line
                type="monotone"
                dataKey="incomePreviousYear"
                name="Entrées N-1"
                stroke="#34d399"
                strokeOpacity={0.45}
                strokeWidth={1.5}
                strokeDasharray="4 3"
                dot={false}
                connectNulls={false}
              />
              <Line
                type="monotone"
                dataKey="expensesPreviousYear"
                name="Sorties N-1"
                stroke="#f87171"
                strokeOpacity={0.45}
                strokeWidth={1.5}
                strokeDasharray="4 3"
                dot={false}
                connectNulls={false}
              />
            </>
          )}

          {frontiere && (
            <ReferenceLine
              x={frontiere.label}
              stroke="rgba(255,255,255,0.35)"
              strokeDasharray="3 3"
              label={{ value: 'banque', position: 'insideTopLeft', fill: 'rgba(255,255,255,0.45)', fontSize: 10 }}
            />
          )}
        </ComposedChart>
      </ResponsiveContainer>

      {moyennes && (
        <p className="text-xs text-white/50 text-center mt-1">
          Moyenne sur {moyennes.n} mois révolus
          {!transfert && (
            <>
              {' · entrées '}<span className="text-white/80 font-medium">{formatCurrency(moyennes.income)}</span>
              {' · sorties '}<span className="text-white/80 font-medium">{formatCurrency(moyennes.expenses)}</span>
            </>
          )}
          {' · net '}
          <span className={moyennes.net >= 0 ? 'text-emerald-400 font-semibold' : 'text-red-400 font-semibold'}>
            {moyennes.net >= 0 ? '+' : '-'}{formatCurrency(Math.abs(moyennes.net))}
          </span>
        </p>
      )}

      {!comparatif && (
        <p className="text-white/30 text-xs mt-1.5 leading-relaxed">
          {data.previousYearAvailableFrom ? (
            <>
              Comparatif N-1 à partir de {dateFr(data.previousYearAvailableFrom)}, quand douze mois de banque
              seront en base. Avant {dateFr(data.firstBankTransactionDate)}, seule la carte Trade Republic est
              importée : les mois à gauche du repère ne portent ni prélèvements ni courses.
            </>
          ) : (
            <>Aucun compte bancaire connecté sur ce tableau de bord, pas de comparatif possible.</>
          )}
        </p>
      )}

      {frontiere && comparatif && (
        <p className="text-white/30 text-xs mt-1.5">
          À gauche du repère « banque », seule la carte Trade Republic est en base.
        </p>
      )}
    </div>
  );
};
