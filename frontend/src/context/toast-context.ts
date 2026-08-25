import { createContext } from 'react';

export type ToastVariant = 'error' | 'success';

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
