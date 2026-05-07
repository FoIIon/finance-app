import { KpiCards } from '../../components/dashboard/KpiCards';
import { BudgetProgressWidget } from '../../components/dashboard/BudgetProgress';
import { RemainingToSpend } from '../../components/dashboard/RemainingToSpend';
import { AccountBalancesWidget } from '../../components/dashboard/AccountBalances';
import { CategoryBreakdownBars } from '../../components/dashboard/CategoryBreakdown';
import { RecentTransactions } from '../../components/dashboard/RecentTransactions';
import { MonthlyTrend } from '../../components/dashboard/MonthlyTrend';
import { SavingsBreakdownWidget } from '../../components/dashboard/SavingsBreakdown';

const DashboardOverview = () => {
  return (
    <div className="space-y-5">
      <KpiCards />

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
        <RemainingToSpend />
        <SavingsBreakdownWidget />
        <AccountBalancesWidget />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
        <BudgetProgressWidget />
        <CategoryBreakdownBars topN={6} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
        <MonthlyTrend />
        <RecentTransactions limit={6} />
      </div>
    </div>
  );
};

export default DashboardOverview;
