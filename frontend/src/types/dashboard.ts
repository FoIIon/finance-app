export interface Dashboard {
  id: number;
  name: string;
  isCreator: boolean;
  isPersonal: boolean;
  createdAt: string;
}

export interface DashboardDetail {
  id: number;
  name: string;
  isCreator: boolean;
  isPersonal: boolean;
  createdAt: string;
  members: DashboardMember[];
  accounts: Account[];
}

export interface DashboardMember {
  userId: number;
  email: string;
  joinedAt: string;
}

export interface Account {
  id: number;
  name: string;
  createdAt: string;
}

export interface CreateDashboard {
  name: string;
}

export interface CreateAccount {
  name: string;
}

export interface Invitation {
  id: number;
  dashboardId: number;
  dashboardName: string;
  invitedEmail: string;
  invitedByEmail: string;
  status: number;
  expiresAt: string;
  createdAt: string;
}
