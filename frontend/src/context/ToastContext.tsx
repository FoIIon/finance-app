import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react';
import { registerToast } from '../api/toastBridge';

export type ToastVariant = 'error' | 'success';

export interface Toast {
  id: number;
  message: string;
  variant: ToastVariant;
}

interface ToastContextType {
  showToast: (message: string, variant?: ToastVariant) => void;
}

const ToastContext = createContext<ToastContextType>({
  showToast: () => {},
});

let counter = 0;

export const ToastProvider = ({ children }: { children: ReactNode }) => {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const removeToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const showToast = useCallback((message: string, variant: ToastVariant = 'error') => {
    const id = ++counter;
    setToasts((prev) => [...prev, { id, message, variant }]);
    setTimeout(() => removeToast(id), 4000);
  }, [removeToast]);

  // Enregistrer dans le bridge pour l'intercepteur axios
  useEffect(() => {
    registerToast(showToast);
  }, [showToast]);

  return (
    <ToastContext.Provider value={{ showToast }}>
      {children}
      {/* Conteneur des toasts — top-right, au-dessus de tout */}
      <div className="fixed top-4 right-4 z-[9999] flex flex-col gap-2 pointer-events-none" aria-live="assertive">
        {toasts.map((toast) => (
          <div
            key={toast.id}
            role="alert"
            className={`pointer-events-auto flex items-center gap-3 px-4 py-3 rounded-xl shadow-lg border backdrop-blur-xl text-sm font-medium max-w-sm animate-[slideInRight_0.3s_ease-out] ${
              toast.variant === 'error'
                ? 'bg-red-900/80 border-red-500/40 text-red-200'
                : 'bg-emerald-900/80 border-emerald-500/40 text-emerald-200'
            }`}
          >
            <span className="text-base flex-shrink-0">
              {toast.variant === 'error' ? '✗' : '✓'}
            </span>
            <span>{toast.message}</span>
            <button
              onClick={() => removeToast(toast.id)}
              className="ml-auto text-white/40 hover:text-white transition-colors flex-shrink-0"
              aria-label="Fermer"
            >
              ×
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
};

export const useToast = () => useContext(ToastContext);
