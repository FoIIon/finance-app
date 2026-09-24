import { createContext } from 'react';

/** « warning » : rien n'a cassé mais l'action n'a pas eu l'effet attendu (relevé déjà en cours, limite atteinte, message laissé en échec). */
export type ToastVariant = 'error' | 'success' | 'warning';

export interface Toast {
  id: number;
  message: string;
  variant: ToastVariant;
}

export interface ToastContextType {
  showToast: (message: string, variant?: ToastVariant) => void;
}

export const ToastContext = createContext<ToastContextType>({
  showToast: () => {},
});
