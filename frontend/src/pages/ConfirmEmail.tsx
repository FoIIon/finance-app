import { useState, useEffect } from 'react';
import { useSearchParams, Link } from 'react-router-dom';
import { authApi } from '../api/auth';

const ConfirmEmail = () => {
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [message, setMessage] = useState('');

  useEffect(() => {
    if (!token) {
      setStatus('error');
      setMessage('Token de confirmation manquant.');
      return;
    }

    const confirm = async () => {
      try {
        const response = await authApi.confirmEmail(token);
        setStatus('success');
        setMessage(response.data.message);
      } catch {
        setStatus('error');
        setMessage('Le lien de confirmation est invalide ou a expiré.');
      }
    };

    confirm();
  }, [token]);

  return (
    <div className="min-h-screen flex items-center justify-center bg-[#0a0a1a] relative overflow-hidden">
      <div className="fixed inset-0 bg-gradient-to-br from-[#0a0a1a] via-[#1a1a3e] to-[#0a0a1a]" />
      <div className="fixed inset-0 opacity-40" style={{
        backgroundImage: 'radial-gradient(circle at 30% 40%, rgba(255, 183, 77, 0.15) 0%, transparent 50%), radial-gradient(circle at 70% 60%, rgba(255, 119, 115, 0.1) 0%, transparent 40%)'
      }} />

      <div className="relative z-10 w-full max-w-md p-8">
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-8 shadow-2xl text-center">
          <h1 className="text-3xl font-bold mb-6 bg-gradient-to-r from-amber-400 to-orange-500 bg-clip-text text-transparent" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
            FinanceApp
          </h1>

          {status === 'loading' && (
            <div className="py-8">
              <div className="w-12 h-12 mx-auto mb-4 border-4 border-amber-500/30 border-t-amber-500 rounded-full animate-spin" />
              <p className="text-white/50">Confirmation en cours...</p>
            </div>
          )}

          {status === 'success' && (
            <div className="py-4">
              <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-emerald-500/10 border border-emerald-500/30 flex items-center justify-center">
                <svg className="w-8 h-8 text-emerald-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                </svg>
              </div>
              <p className="text-white/80 mb-6">{message}</p>
              <Link
                to="/login"
                className="inline-block py-3 px-8 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200"
              >
                Se connecter
              </Link>
            </div>
          )}

          {status === 'error' && (
            <div className="py-4">
              <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-red-500/10 border border-red-500/30 flex items-center justify-center">
                <svg className="w-8 h-8 text-red-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                </svg>
              </div>
              <p className="text-white/80 mb-6">{message}</p>
              <Link
                to="/login"
                className="block text-amber-400 hover:text-amber-300 transition-colors text-sm"
              >
                Retour à la connexion
              </Link>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default ConfirmEmail;
