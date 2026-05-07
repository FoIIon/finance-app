import { useState } from 'react';
import { Link, useLocation, Outlet } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';
import DashboardSelector from './DashboardSelector';

const navItems = [
  { path: '/dashboard/overview', label: 'Tableau de bord', icon: '📊' },
  { path: '/transactions', label: 'Transactions', icon: '💳' },
  { path: '/budgets', label: 'Budgets', icon: '💰' },
  { path: '/categories', label: 'Catégories', icon: '🏷️' },
  { path: '/recurring', label: 'Récurrentes', icon: '🔁' },
  { path: '/bank', label: 'Banques', icon: '🏦' },
  { path: '/dashboard-settings', label: 'Paramètres', icon: '⚙️' },
];

interface SidebarContentProps {
  email: string | null;
  pathname: string;
  onNavClick: () => void;
  onLogout: () => void;
}

const SidebarContent = ({ email, pathname, onNavClick, onLogout }: SidebarContentProps) => (
  <div className="flex flex-col h-full">
    <div className="p-6 border-b border-white/10">
      <h1
        className="text-2xl font-bold bg-gradient-to-r from-amber-400 to-orange-500 bg-clip-text text-transparent"
        style={{ fontFamily: "'Space Grotesk', sans-serif" }}
      >
        FinanceApp
      </h1>
      <p className="text-sm text-white/40 mt-1">{email}</p>
    </div>

    <div className="p-4 border-b border-white/10">
      <DashboardSelector />
    </div>

    <div className="flex-1 p-4 space-y-1">
      {navItems.map((item) => {
        const isActive = item.path.startsWith('/dashboard')
          ? pathname.startsWith('/dashboard')
          : pathname === item.path;
        return (
          <Link
            key={item.path}
            to={item.path}
            onClick={onNavClick}
            className={`flex items-center gap-3 px-4 py-3 rounded-xl transition-all duration-200 ${
              isActive
                ? 'bg-gradient-to-r from-amber-500/20 to-orange-500/20 text-amber-400 border border-amber-500/30'
                : 'text-white/60 hover:text-white/90 hover:bg-white/5'
            }`}
          >
            <span className="text-lg">{item.icon}</span>
            <span className="font-medium">{item.label}</span>
          </Link>
        );
      })}
    </div>

    <div className="p-4 border-t border-white/10">
      <button
        onClick={onLogout}
        className="w-full flex items-center gap-3 px-4 py-3 rounded-xl text-white/40 hover:text-red-400 hover:bg-red-500/10 transition-all duration-200"
      >
        <span>🚪</span>
        <span className="font-medium">Déconnexion</span>
      </button>
    </div>
  </div>
);

const Layout = () => {
  const { email, logout } = useAuth();
  const location = useLocation();
  const [sidebarOpen, setSidebarOpen] = useState(false);

  const closeSidebar = () => setSidebarOpen(false);

  return (
    <div className="min-h-screen bg-[#0a0a1a]">
      {/* Fond atmosphérique */}
      <div className="fixed inset-0 bg-gradient-to-br from-[#0a0a1a] via-[#1a1a3e] to-[#0a0a1a]" />
      <div
        className="fixed inset-0 opacity-30"
        style={{
          backgroundImage:
            'radial-gradient(circle at 20% 50%, rgba(120, 119, 198, 0.15) 0%, transparent 50%), radial-gradient(circle at 80% 20%, rgba(255, 119, 115, 0.1) 0%, transparent 40%)',
        }}
      />

      {/* Sidebar desktop (md+) */}
      <nav className="hidden md:flex fixed left-0 top-0 h-full w-64 bg-white/5 backdrop-blur-xl border-r border-white/10 z-50 flex-col">
        <SidebarContent
          email={email}
          pathname={location.pathname}
          onNavClick={closeSidebar}
          onLogout={logout}
        />
      </nav>

      {/* Backdrop mobile */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 bg-black/60 backdrop-blur-sm z-40 md:hidden"
          onClick={closeSidebar}
          aria-hidden="true"
        />
      )}

      {/* Sidebar mobile (overlay coulissant) */}
      <nav
        className={`fixed left-0 top-0 h-full w-72 bg-[#1a1a3e] border-r border-white/10 z-50 flex flex-col md:hidden transition-transform duration-300 ease-in-out ${
          sidebarOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
        aria-label="Navigation principale"
      >
        {/* Bouton fermer */}
        <button
          onClick={closeSidebar}
          className="absolute top-4 right-4 p-2 text-white/40 hover:text-white transition-colors"
          aria-label="Fermer le menu"
        >
          <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
        <SidebarContent
          email={email}
          pathname={location.pathname}
          onNavClick={closeSidebar}
          onLogout={logout}
        />
      </nav>

      {/* Header mobile avec bouton hamburger */}
      <header className="fixed top-0 left-0 right-0 z-30 md:hidden bg-[#0a0a1a]/90 backdrop-blur-xl border-b border-white/10 px-4 py-3 flex items-center gap-3">
        <button
          onClick={() => setSidebarOpen(true)}
          aria-label="Ouvrir le menu"
          className="p-2 rounded-xl text-white/60 hover:text-white hover:bg-white/10 transition-all"
        >
          <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M4 6h16M4 12h16M4 18h16" />
          </svg>
        </button>
        <h1
          className="text-lg font-bold bg-gradient-to-r from-amber-400 to-orange-500 bg-clip-text text-transparent"
          style={{ fontFamily: "'Space Grotesk', sans-serif" }}
        >
          FinanceApp
        </h1>
      </header>

      {/* Contenu principal */}
      <main className="md:ml-64 relative z-10 p-4 md:p-8 pt-16 md:pt-8">
        <Outlet />
      </main>
    </div>
  );
};

export default Layout;
