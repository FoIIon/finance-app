import { useState } from 'react';
import { useSearchParams, Link } from 'react-router-dom';
import { authApi } from '../api/auth';

const RegisterSuccess = () => {
  const [searchParams] = useSearchParams();
  const email = searchParams.get('email') ?? '';
  const [resending, setResending] = useState(false);
  const [resendMessage, setResendMessage] = useState('');

  const handleResend = async () => {
    if (!email) return;
    setResending(true);
    setResendMessage('');
    try {
      const response = await authApi.resendConfirmation(email);
      setResendMessage(response.data.message);
    } catch {
      setResendMessage('Erreur lors de l\'envoi. Réessayez plus tard.');
    } finally {
      setResending(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-[#0a0a1a] relative overflow-hidden">
      <div className="fixed inset-0 bg-gradient-to-br from-[#0a0a1a] via-[#1a1a3e] to-[#0a0a1a]" />
      <div className="fixed inset-0 opacity-40" style={{
        backgroundImage: 'radial-gradient(circle at 30% 40%, rgba(255, 183, 77, 0.15) 0%, transparent 50%), radial-gradient(circle at 70% 60%, rgba(255, 119, 115, 0.1) 0%, transparent 40%)'
      }} />

      <div className="relative z-10 w-full max-w-md p-8">
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-8 shadow-2xl text-center">
          <h1 className="text-3xl font-bold mb-2 bg-gradient-to-r from-amber-400 to-orange-500 bg-clip-text text-transparent" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
            FinanceApp
          </h1>

          <div className="mt-6 mb-4">
            <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-amber-500/10 border border-amber-500/30 flex items-center justify-center">
              <svg className="w-8 h-8 text-amber-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
              </svg>
            </div>
            <h2 className="text-xl font-semibold text-white mb-2">Vérifiez votre email</h2>
            <p className="text-white/50 text-sm">
              Un email de confirmation a été envoyé{email ? ` à ${email}` : ''}. Cliquez sur le lien dans l'email pour activer votre compte.
            </p>
          </div>

          {resendMessage && (
            <div className="mb-4 p-3 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-300 text-sm">
              {resendMessage}
            </div>
          )}

          {email && (
            <button
              onClick={handleResend}
              disabled={resending}
              className="w-full py-3 rounded-xl bg-white/5 border border-white/10 text-white/70 hover:bg-white/10 hover:text-white transition-all duration-200 disabled:opacity-50 mb-4"
            >
              {resending ? 'Envoi...' : 'Renvoyer l\'email de confirmation'}
            </button>
          )}

          <Link
            to="/login"
            className="block text-amber-400 hover:text-amber-300 transition-colors text-sm"
          >
            Retour à la connexion
          </Link>
        </div>
      </div>
    </div>
  );
};

export default RegisterSuccess;
