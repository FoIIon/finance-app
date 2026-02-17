import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';
import { authApi } from '../api/auth';

const Register = () => {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();
  const navigate = useNavigate();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (password !== confirmPassword) {
      setError('Les mots de passe ne correspondent pas.');
      return;
    }
    setLoading(true);
    setError('');
    try {
      const response = await authApi.register({ email, password });
      login(response.data.token, response.data.email);
      navigate('/');
    } catch {
      setError('Erreur lors de la création du compte.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-[#0a0a1a] relative overflow-hidden">
      <div className="fixed inset-0 bg-gradient-to-br from-[#0a0a1a] via-[#1a1a3e] to-[#0a0a1a]" />
      <div className="fixed inset-0 opacity-40" style={{
        backgroundImage: 'radial-gradient(circle at 30% 40%, rgba(255, 183, 77, 0.15) 0%, transparent 50%), radial-gradient(circle at 70% 60%, rgba(255, 119, 115, 0.1) 0%, transparent 40%)'
      }} />

      <div className="relative z-10 w-full max-w-md p-8">
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-8 shadow-2xl">
          <h1 className="text-3xl font-bold text-center mb-2 bg-gradient-to-r from-amber-400 to-orange-500 bg-clip-text text-transparent" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
            FinanceApp
          </h1>
          <p className="text-center text-white/40 mb-8">Créer votre compte</p>

          {error && (
            <div className="mb-4 p-3 rounded-xl bg-red-500/10 border border-red-500/30 text-red-400 text-sm">
              {error}
            </div>
          )}

          <form onSubmit={handleSubmit} className="space-y-5">
            <div>
              <label htmlFor="register-email" className="block text-sm text-white/60 mb-2">Email</label>
              <input
                id="register-email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                className="w-full px-4 py-3 rounded-xl bg-white/5 border border-white/10 text-white placeholder-white/30 focus:outline-none focus:border-amber-500/50 focus:ring-1 focus:ring-amber-500/50 transition-all"
                placeholder="votre@email.com"
              />
            </div>
            <div>
              <label htmlFor="register-password" className="block text-sm text-white/60 mb-2">Mot de passe</label>
              <input
                id="register-password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                minLength={8}
                className="w-full px-4 py-3 rounded-xl bg-white/5 border border-white/10 text-white placeholder-white/30 focus:outline-none focus:border-amber-500/50 focus:ring-1 focus:ring-amber-500/50 transition-all"
                placeholder="••••••••"
              />
            </div>
            <div>
              <label htmlFor="register-confirm-password" className="block text-sm text-white/60 mb-2">Confirmer le mot de passe</label>
              <input
                id="register-confirm-password"
                type="password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                required
                minLength={8}
                className="w-full px-4 py-3 rounded-xl bg-white/5 border border-white/10 text-white placeholder-white/30 focus:outline-none focus:border-amber-500/50 focus:ring-1 focus:ring-amber-500/50 transition-all"
                placeholder="••••••••"
              />
            </div>
            <button
              type="submit"
              disabled={loading}
              className="w-full py-3 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200 disabled:opacity-50"
            >
              {loading ? 'Création...' : 'Créer un compte'}
            </button>
          </form>

          <p className="mt-6 text-center text-white/40 text-sm">
            Déjà un compte ?{' '}
            <Link to="/login" className="text-amber-400 hover:text-amber-300 transition-colors">
              Se connecter
            </Link>
          </p>
        </div>
      </div>
    </div>
  );
};

export default Register;
