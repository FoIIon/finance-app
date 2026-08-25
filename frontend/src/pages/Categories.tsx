import { useState } from 'react';
import { categoriesApi } from '../api/categories';
import { useCategoriesQuery } from '../hooks/queries';
import type { Category, CreateCategory } from '../types/category';

// Palette de couleurs distinctes pour les nouvelles catégories
const COLOR_PALETTE = [
  '#E74C3C', '#3498DB', '#2ECC71', '#F39C12', '#9B59B6',
  '#1ABC9C', '#E67E22', '#2980B9', '#27AE60', '#8E44AD',
  '#D35400', '#16A085', '#C0392B', '#2C3E50', '#F1C40F',
  '#7F8C8D', '#E84393', '#00CEC9', '#6C5CE7', '#FD79A8',
];

const getNextColor = (existingCategories: Category[]) => {
  const usedColors = new Set(existingCategories.map((c) => c.color.toUpperCase()));
  const available = COLOR_PALETTE.find((c) => !usedColors.has(c.toUpperCase()));
  return available ?? COLOR_PALETTE[existingCategories.length % COLOR_PALETTE.length];
};

const Categories = () => {
  const { data: categories = [], refetch: refetchCategories, isError: loadFailed } = useCategoriesQuery();
  const [showForm, setShowForm] = useState(false);
  const [editingCategory, setEditingCategory] = useState<Category | null>(null);
  const [formData, setFormData] = useState<CreateCategory>({ name: '', icon: '', color: '#E74C3C' });
  const [deleteConfirm, setDeleteConfirm] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadError = loadFailed ? 'Erreur lors du chargement des catégories' : null;

  const openCreateForm = () => {
    setEditingCategory(null);
    setFormData({ name: '', icon: '', color: getNextColor(categories) });
    setShowForm(true);
  };

  const openEditForm = (c: Category) => {
    setEditingCategory(c);
    setFormData({ name: c.name, icon: c.icon, color: c.color });
    setShowForm(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    try {
      if (editingCategory) {
        await categoriesApi.update(editingCategory.id, formData);
      } else {
        await categoriesApi.create(formData);
      }
      setFormData({ name: '', icon: '', color: getNextColor(categories) });
      setEditingCategory(null);
      setShowForm(false);
      refetchCategories();
    } catch {
      setError('Erreur lors de la sauvegarde de la catégorie');
    }
  };

  const handleCancel = () => {
    setEditingCategory(null);
    setShowForm(false);
  };

  const handleDelete = async (id: number) => {
    setError(null);
    try {
      await categoriesApi.delete(id);
      setDeleteConfirm(null);
      refetchCategories();
    } catch {
      setDeleteConfirm(null);
      setError('Impossible de supprimer : cette catégorie est peut-être utilisée par des transactions.');
    }
  };

  return (
    <div className="space-y-6 animate-[fadeIn_0.5s_ease-out]">
      <div className="flex items-center justify-between">
        <h2 className="text-3xl font-bold text-white" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
          Catégories
        </h2>
        <button
          onClick={openCreateForm}
          className="px-5 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200"
        >
          + Ajouter
        </button>
      </div>

      {/* Formulaire inline */}
      {showForm && (
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-6">
          <h3 className="text-sm font-medium text-white/60 mb-3">
            {editingCategory ? 'Modifier la catégorie' : 'Nouvelle catégorie'}
          </h3>
          <form onSubmit={handleSubmit} className="flex gap-4 items-end">
            <div className="flex-1">
              <label className="block text-sm text-white/60 mb-1">Nom</label>
              <input
                type="text"
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                required
                className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                placeholder="Nom de la catégorie"
              />
            </div>
            <div className="w-28">
              <label className="block text-sm text-white/60 mb-1">Icône</label>
              <input
                type="text"
                value={formData.icon}
                onChange={(e) => setFormData({ ...formData, icon: e.target.value })}
                required
                className="w-full px-4 py-2.5 rounded-xl bg-white/5 border border-white/10 text-white focus:outline-none focus:border-amber-500/50"
                placeholder="🎯"
              />
            </div>
            <div className="w-24">
              <label className="block text-sm text-white/60 mb-1">Couleur</label>
              <input
                type="color"
                value={formData.color}
                onChange={(e) => setFormData({ ...formData, color: e.target.value })}
                className="w-full h-[42px] rounded-xl bg-white/5 border border-white/10 cursor-pointer"
              />
            </div>
            <button type="submit" className="px-6 py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all">
              {editingCategory ? 'Enregistrer' : 'Créer'}
            </button>
            <button type="button" onClick={handleCancel} className="px-4 py-2.5 rounded-xl border border-white/10 text-white/60 hover:text-white hover:bg-white/5 transition-all">
              Annuler
            </button>
          </form>
        </div>
      )}

      {(error ?? loadError) && (
        <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-sm">
          {error ?? loadError}
        </div>
      )}

      {/* Liste des catégories */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {categories.map((c) => (
          <div
            key={c.id}
            className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-5 flex items-center justify-between hover:border-white/20 transition-all duration-200"
          >
            <div className="flex items-center gap-3">
              <div
                className="w-10 h-10 rounded-xl flex items-center justify-center text-xl"
                style={{ backgroundColor: c.color + '20' }}
              >
                {c.icon}
              </div>
              <div>
                <p className="text-white font-medium">{c.name}</p>
                <p className="text-white/30 text-xs">{c.isDefault ? 'Par défaut' : 'Personnalisée'}</p>
              </div>
            </div>
            {!c.isDefault && (
              <div className="flex items-center gap-2">
                <button aria-label="Modifier" onClick={() => openEditForm(c)} className="text-white/30 hover:text-amber-400 transition-colors">✏️</button>
                {deleteConfirm === c.id ? (
                  <>
                    <button onClick={() => handleDelete(c.id)} className="text-red-400 hover:text-red-300 text-sm font-medium">Oui</button>
                    <button onClick={() => setDeleteConfirm(null)} className="text-white/40 hover:text-white text-sm">Non</button>
                  </>
                ) : (
                  <button aria-label="Supprimer" onClick={() => setDeleteConfirm(c.id)} className="text-white/30 hover:text-red-400 transition-colors">🗑️</button>
                )}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
};

export default Categories;
