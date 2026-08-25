import { PieChart, Pie, Cell, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import { InvestmentKind } from '../../types/investment';
import type { Investment } from '../../types/investment';
import { formatCurrency } from '../../utils/format';

// Palette validée, ordre fixe. Ne pas en dévier.
const KIND_COLORS: Record<number, string> = {
  [InvestmentKind.Security]: '#6366f1',
  [InvestmentKind.Metal]: '#d97706',
  [InvestmentKind.InsuranceContract]: '#059669',
  [InvestmentKind.Crypto]: '#8b5cf6',
};

const KIND_LABELS: Record<number, string> = {
  [InvestmentKind.Security]: 'Titre coté',
  [InvestmentKind.Metal]: 'Métal',
  [InvestmentKind.InsuranceContract]: 'Assurance-vie',
  [InvestmentKind.Crypto]: 'Crypto',
};

const HOLDER_PALETTE = ['#6366f1', '#d97706', '#059669', '#ef4444', '#8b5cf6'];

interface Slice {
  name: string;
  value: number;
  color: string;
}

const RADIAN = Math.PI / 180;

// Labels directs hors du donut, jamais colorés de la couleur du segment : avec le
// paddingAngle, ils compensent un écart daltonisme limite ambre/émeraude.
const renderSegmentLabel = (props: unknown) => {
  const { cx, cy, midAngle, outerRadius, percent, name } = props as {
    cx: number;
    cy: number;
    midAngle: number;
    outerRadius: number;
    percent: number;
    name: string;
  };
  const r = outerRadius + 14;
  const x = cx + r * Math.cos(-midAngle * RADIAN);
  const y = cy + r * Math.sin(-midAngle * RADIAN);
  return (
    <text
      x={x}
      y={y}
      fill="rgba(255,255,255,0.7)"
      fontSize={11}
      textAnchor={x > cx ? 'start' : 'end'}
      dominantBaseline="central"
    >
      {name} {(percent * 100).toFixed(0)} %
    </text>
  );
};

const Donut = ({ title, data }: { title: string; data: Slice[] }) => (
  <div>
    <h4 className="text-sm font-medium text-white/70 mb-1">{title}</h4>
    {data.length === 0 ? (
      <p className="text-white/30 text-center py-10 text-sm">Aucune ligne valorisée</p>
    ) : data.length === 1 ? (
      // Un camembert à une seule part est un rectangle compliqué : il occupe la hauteur
      // d'un graphique pour n'apprendre qu'une chose, laquelle tient sur une ligne.
      <p className="text-white/60 py-10 text-sm text-center">
        <span className="text-white/90">{data[0].name}</span> porte la totalité,{' '}
        {formatCurrency(data[0].value)}
      </p>
    ) : (
      <ResponsiveContainer width="100%" height={230}>
        <PieChart>
          <Pie
            data={data}
            dataKey="value"
            nameKey="name"
            innerRadius={48}
            outerRadius={70}
            paddingAngle={2}
            label={renderSegmentLabel}
            labelLine={{ stroke: 'rgba(255,255,255,0.2)' }}
            isAnimationActive={false}
          >
            {data.map((d) => (
              <Cell key={d.name} fill={d.color} stroke="none" />
            ))}
          </Pie>
          <Tooltip
            contentStyle={{
              backgroundColor: '#1a1a3e',
              border: '1px solid rgba(255,255,255,0.1)',
              borderRadius: '12px',
              color: '#fff',
            }}
            formatter={(value) => formatCurrency(value as number)}
          />
          <Legend wrapperStyle={{ fontSize: '11px', color: 'rgba(255,255,255,0.6)' }} />
        </PieChart>
      </ResponsiveContainer>
    )}
  </div>
);

export const AllocationCharts = ({ investments }: { investments: Investment[] }) => {
  const active = investments.filter((i) => !i.isArchived);
  const valued = active.filter((i) => i.marketValue != null);
  const excludedCount = active.length - valued.length;

  const byKind: Slice[] = Object.values(InvestmentKind)
    .map((kind) => ({
      name: KIND_LABELS[kind],
      value: valued.filter((i) => i.kind === kind).reduce((s, i) => s + (i.marketValue ?? 0), 0),
      color: KIND_COLORS[kind],
    }))
    .filter((s) => s.value > 0);

  // Couleur attribuée sur l'ensemble des titulaires actifs, pas seulement les valorisés :
  // la couleur suit l'entité, un filtre ne repeint jamais les survivants.
  const holders = [...new Set(active.map((i) => i.holder))].sort((a, b) => a.localeCompare(b, 'fr'));
  const holderColor = new Map(holders.map((h, idx) => [h, HOLDER_PALETTE[idx % HOLDER_PALETTE.length]]));

  const byHolder: Slice[] = holders
    .map((h) => ({
      name: h,
      value: valued.filter((i) => i.holder === h).reduce((s, i) => s + (i.marketValue ?? 0), 0),
      color: holderColor.get(h)!,
    }))
    .filter((s) => s.value > 0);

  return (
    <div className="bg-[#1a1a3e] rounded-2xl border border-white/10 p-5">
      <h3 className="text-base md:text-lg font-semibold text-white mb-4">Répartition</h3>
      <div className="grid gap-6 md:grid-cols-2">
        <Donut title="Par type d'actif" data={byKind} />
        <Donut title="Par titulaire" data={byHolder} />
      </div>
      {excludedCount > 0 && (
        <p className="text-xs text-white/40 mt-2">
          {excludedCount} ligne{excludedCount > 1 ? 's' : ''} non valorisée{excludedCount > 1 ? 's' : ''} exclue{excludedCount > 1 ? 's' : ''}
        </p>
      )}
    </div>
  );
};
