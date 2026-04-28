/**
 * Bridge permettant à l'intercepteur axios (hors React) d'afficher des toasts.
 * Le ToastProvider enregistre sa fonction showToast ici au montage.
 */

type ShowToastFn = (message: string, variant?: 'error' | 'success') => void;

let _showToast: ShowToastFn | null = null;

export const registerToast = (fn: ShowToastFn) => {
  _showToast = fn;
};

export const showToastOutsideReact = (message: string, variant: 'error' | 'success' = 'error') => {
  if (_showToast) {
    _showToast(message, variant);
  }
};
