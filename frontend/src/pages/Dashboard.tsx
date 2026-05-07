import { Outlet, NavLink } from 'react-router-dom';
import { useDashboards } from '../hooks/useDashboards';
import { PeriodSelector } from '../components/dashboard/PeriodSelector';
import { BankAccountSelector } from '../components/dashboard/BankAccountSelector';
import { useAccountBalancesQuery } from '../hooks/queries';
import { usePeriod } from '../context/PeriodContext';

const tabs = [
  { to: 'overview', label: 'Ce mois-ci', icon: '📊' },
  { to: 'categories', label: 'Catégories', icon: '📂' },
  { to: 'projects', label: 'Projets', icon: '🏖️' },
  { to: 'triage', label: 'À traiter', icon: '✅' },
];

const Dashboard = () => {
  const { currentDashboard } = useDashboards();
  const { bankAccountFilter, period } = usePeriod();
  const { data: accounts } = useAccountBalancesQuery(currentDashboard?.id);

  const accountCount = accounts?.length ?? 0;
  const filteredAccount = bankAccountFilter ? accounts?.find(a => a.accountId === bankAccountFilter) : null;
  const scopeLabel = filteredAccount
    ? `${filteredAccount.bankInstitutionName ?? 'Compte'} · ${period.label.toLowerCase()}`
    : accountCount > 1
    ? `${accountCount} comptes consolidés · ${period.label.toLowerCase()}`
    : period.label;

  return (
    <div className="space-y-5 md:space-y-6 animate-[fadeIn_0.15s_ease-out]">
      <div className="flex flex-col md:flex-row md:items-end md:justify-between gap-3 md:gap-4">
        <div>
          <h2 className="text-2xl md:text-3xl font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
            {currentDashboard?.name ?? 'Tableau de bord'}
          </h2>
          <p className="text-white/40 text-xs md:text-sm mt-1">{scopeLabel}</p>
        </div>
        <div className="flex flex-col items-end gap-2">
          <BankAccountSelector />
          <PeriodSelector />
        </div>
      </div>

      {/* Tabs — top sur md+, bottom-fixed sur mobile */}
      <nav className="hidden md:flex gap-1 p-1 rounded-2xl bg-white/5 border border-white/10 w-fit">
        {tabs.map((t) => (
          <NavLink
            key={t.to}
            to={t.to}
            className={({ isActive }) =>
              `px-4 py-2 rounded-xl text-sm font-medium transition-all flex items-center gap-2 ${
                isActive
                  ? 'bg-amber-500/20 text-amber-300 border border-amber-500/30'
                  : 'text-white/60 hover:text-white hover:bg-white/5 border border-transparent'
              }`
            }
          >
            <span aria-hidden="true">{t.icon}</span>
            <span>{t.label}</span>
          </NavLink>
        ))}
      </nav>

      <div className="pb-20 md:pb-0">
        <Outlet />
      </div>

      {/* Bottom-nav mobile */}
      <nav className="md:hidden fixed bottom-0 left-0 right-0 bg-[#0a0a1a]/95 backdrop-blur-xl border-t border-white/10 z-30">
        <div className="grid grid-cols-4 gap-1 p-2">
          {tabs.map((t) => (
            <NavLink
              key={t.to}
              to={t.to}
              className={({ isActive }) =>
                `flex flex-col items-center justify-center py-2 rounded-xl transition-all ${
                  isActive
                    ? 'bg-amber-500/20 text-amber-300'
                    : 'text-white/60'
                }`
              }
            >
              <span className="text-lg" aria-hidden="true">{t.icon}</span>
              <span className="text-[10px] font-medium mt-0.5">{t.label}</span>
            </NavLink>
          ))}
        </div>
      </nav>
    </div>
  );
};

export default Dashboard;
