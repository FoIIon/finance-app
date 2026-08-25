import { createContext } from 'react';
import type { AuthState } from '../types/auth';

export interface AuthContextType extends AuthState {
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
