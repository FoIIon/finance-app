import { useContext } from 'react';
import { ToastContext } from '../context/toast-context';

export const useToast = () => useContext(ToastContext);
