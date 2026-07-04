import axios from 'axios';
import { showToastOutsideReact } from './toastBridge';

// En prod le frontend est servi par le backend : même origine, URL relative.
// En dev, Vite tourne sur 5173 et l'API sur 5000.
const apiClient = axios.create({
  baseURL: import.meta.env.PROD ? '/api' : 'http://localhost:5000/api',
});

apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401 && !error.config?.url?.startsWith('/auth/')) {
      showToastOutsideReact('Session expirée, reconnexion...', 'error');
      localStorage.removeItem('token');
      localStorage.removeItem('email');
      setTimeout(() => {
        window.location.href = '/login';
      }, 1500);
    }
    return Promise.reject(error);
  }
);

export default apiClient;
