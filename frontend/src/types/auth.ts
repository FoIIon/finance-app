export interface LoginCredentials {
  email: string;
  password: string;
}

export interface RegisterCredentials {
  email: string;
  password: string;
}

export interface AuthResponse {
  token: string;
  email: string;
}

export interface AuthState {
  token: string | null;
  email: string | null;
  isAuthenticated: boolean;
}
