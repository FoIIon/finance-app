import { useState, useEffect } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';
import { invitationsApi } from '../api/invitations';
import { useDashboards } from '../hooks/useDashboards';

const AcceptInvitation = () => {
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');
  const { isAuthenticated } = useAuth();
  const { refreshDashboards } = useDashboards();
  const navigate = useNavigate();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [message, setMessage] = useState('');

  useEffect(() => {
    if (!token) {
      setStatus('error');
      setMessage('Token d\'invitation manquant.');
      return;
    }

    if (!isAuthenticated) {
      // Stocker le token pour traitement post-login
      sessionStorage.setItem('pendingInvitationToken', token);
      navigate('/login');
      return;
    }

    const accept = async () => {
      try {
        const response = await invitationsApi.accept(token);
        setStatus('success');
        setMessage(response.data.message);
        await refreshDashboards();
      } catch {
        setStatus('error');
        setMessage('L\'invitation est invalide ou a expiré.');
      }
    };

    accept();
  }, [token, isAuthenticated]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div className="flex items-center justify-center min-h-[60vh]">
      <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 p-8 shadow-2xl text-center max-w-md w-full">
        <h1 className="text-2xl font-bold mb-6 bg-gradient-to-r from-amber-400 to-orange-500 bg-clip-text text-transparent" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
          Invitation au dashboard
        </h1>

        {status === 'loading' && (
          <div className="py-8">
            <div className="w-12 h-12 mx-auto mb-4 border-4 border-amber-500/30 border-t-amber-500 rounded-full animate-spin" />
            <p className="text-white/50">Traitement en cours...</p>
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
            <button
              onClick={() => navigate('/')}
              className="inline-block py-3 px-8 rounded-xl bg-gradient-to-r from-amber-500 to-orange-600 text-white font-semibold hover:from-amber-600 hover:to-orange-700 transition-all duration-200"
            >
              Aller au dashboard
            </button>
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
            <button
              onClick={() => navigate('/')}
              className="text-amber-400 hover:text-amber-300 transition-colors text-sm"
            >
              Retour au dashboard
            </button>
          </div>
        )}
      </div>
    </div>
  );
};

export default AcceptInvitation;
