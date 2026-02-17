import { createContext, useState, type ReactNode } from 'react';
import type { AuthState } from '../types/auth';

interface AuthContextType extends AuthState {
  login: (token: string, email: string) => void;
  logout: () => void;
}

export const AuthContext = createContext<AuthContextType>({
  token: null,
  email: null,
  isAuthenticated: false,
  login: () => {},
  logout: () => {},
});

export const AuthProvider = ({ children }: { children: ReactNode }) => {
  const [authState, setAuthState] = useState<AuthState>({
    token: localStorage.getItem('token'),
    email: localStorage.getItem('email'),
    isAuthenticated: !!localStorage.getItem('token'),
  });

  const login = (token: string, email: string) => {
    localStorage.setItem('token', token);
    localStorage.setItem('email', email);
    setAuthState({ token, email, isAuthenticated: true });
  };

  const logout = () => {
    localStorage.removeItem('token');
    localStorage.removeItem('email');
    setAuthState({ token: null, email: null, isAuthenticated: false });
  };

  return (
    <AuthContext.Provider value={{ ...authState, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
};
