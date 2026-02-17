import { useState } from 'react';
import { useDashboards } from '../hooks/useDashboards';
import { dashboardsApi } from '../api/dashboards';

const DashboardSelector = () => {
  const { dashboards, currentDashboard, setCurrentDashboard, refreshDashboards } = useDashboards();
  const [showDropdown, setShowDropdown] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [creating, setCreating] = useState(false);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newName.trim()) return;
    setCreating(true);
    try {
      const response = await dashboardsApi.create({ name: newName.trim() });
      await refreshDashboards();
      setCurrentDashboard(response.data);
      setNewName('');
      setShowCreate(false);
      setShowDropdown(false);
    } catch {
      // Silently fail
    } finally {
      setCreating(false);
    }
  };

  if (!currentDashboard) return null;

  return (
    <div className="relative">
      <button
        onClick={() => setShowDropdown(!showDropdown)}
        className="w-full flex items-center justify-between gap-2 px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white hover:bg-white/10 transition-all"
      >
        <span className="truncate text-sm font-medium">{currentDashboard.name}</span>
        <svg className={`w-4 h-4 text-white/40 transition-transform ${showDropdown ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {showDropdown && (
        <>
          <div className="fixed inset-0 z-40" onClick={() => { setShowDropdown(false); setShowCreate(false); }} />
          <div className="absolute left-0 right-0 top-full mt-1 bg-[#1a1a3e] border border-white/10 rounded-xl shadow-2xl z-50 overflow-hidden">
            {dashboards.map((d) => (
              <button
                key={d.id}
                onClick={() => {
                  setCurrentDashboard(d);
                  setShowDropdown(false);
                }}
                className={`w-full text-left px-4 py-2.5 text-sm transition-colors ${
                  d.id === currentDashboard.id
                    ? 'bg-amber-500/20 text-amber-400'
                    : 'text-white/70 hover:bg-white/5 hover:text-white'
                }`}
              >
                {d.name}
                {!d.isCreator && <span className="ml-2 text-xs text-white/30">(partagé)</span>}
              </button>
            ))}

            <div className="border-t border-white/10">
              {showCreate ? (
                <form onSubmit={handleCreate} className="p-3 flex gap-2">
                  <input
                    type="text"
                    value={newName}
                    onChange={(e) => setNewName(e.target.value)}
                    placeholder="Nom du dashboard"
                    autoFocus
                    className="flex-1 px-3 py-1.5 rounded-lg bg-white/5 border border-white/10 text-white text-sm placeholder-white/30 focus:outline-none focus:border-amber-500/50"
                  />
                  <button
                    type="submit"
                    disabled={creating}
                    className="px-3 py-1.5 rounded-lg bg-amber-500/20 text-amber-400 text-sm font-medium hover:bg-amber-500/30 transition-colors disabled:opacity-50"
                  >
                    OK
                  </button>
                </form>
              ) : (
                <button
                  onClick={() => setShowCreate(true)}
                  className="w-full text-left px-4 py-2.5 text-sm text-amber-400 hover:bg-amber-500/10 transition-colors"
                >
                  + Nouveau dashboard
                </button>
              )}
            </div>
          </div>
        </>
      )}
    </div>
  );
};

export default DashboardSelector;
