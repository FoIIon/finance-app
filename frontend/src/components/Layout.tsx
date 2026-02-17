import { Link, useLocation, Outlet } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';

const navItems = [
  { path: '/', label: 'Tableau de bord', icon: '📊' },
  { path: '/transactions', label: 'Transactions', icon: '💳' },
  { path: '/categories', label: 'Catégories', icon: '🏷️' },
];

const Layout = () => {
  const { email, logout } = useAuth();
  const location = useLocation();

  return (
    <div className="min-h-screen bg-[#0a0a1a]">
      {/* Fond atmosphérique */}
      <div className="fixed inset-0 bg-gradient-to-br from-[#0a0a1a] via-[#1a1a3e] to-[#0a0a1a]" />
      <div className="fixed inset-0 opacity-30" style={{
        backgroundImage: 'radial-gradient(circle at 20% 50%, rgba(120, 119, 198, 0.15) 0%, transparent 50%), radial-gradient(circle at 80% 20%, rgba(255, 119, 115, 0.1) 0%, transparent 40%)'
      }} />

      {/* Sidebar */}
      <nav className="fixed left-0 top-0 h-full w-64 bg-white/5 backdrop-blur-xl border-r border-white/10 z-50 flex flex-col">
        <div className="p-6 border-b border-white/10">
          <h1 className="text-2xl font-bold bg-gradient-to-r from-amber-400 to-orange-500 bg-clip-text text-transparent" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
            FinanceApp
          </h1>
          <p className="text-sm text-white/40 mt-1">{email}</p>
        </div>

        <div className="flex-1 p-4 space-y-1">
          {navItems.map((item) => {
            const isActive = location.pathname === item.path;
            return (
              <Link
                key={item.path}
                to={item.path}
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
            onClick={logout}
            className="w-full flex items-center gap-3 px-4 py-3 rounded-xl text-white/40 hover:text-red-400 hover:bg-red-500/10 transition-all duration-200"
          >
            <span>🚪</span>
            <span className="font-medium">Déconnexion</span>
          </button>
        </div>
      </nav>

      {/* Contenu principal */}
      <main className="ml-64 relative z-10 p-8">
        <Outlet />
      </main>
    </div>
  );
};

export default Layout;
