// Lecture des champs du formulaire d'échéance et des refus du serveur. Aucune règle métier : le serveur
// valide (longueur, plage), on ne fait que lire ce qu'il répond et l'écrire sous le bon champ.
import { isAxiosError } from 'axios';
import { toIso } from '../agenda/agendaFormat';

export interface FieldErrors {
  label?: string;
  dueDate?: string;
  amount?: string;
  general?: string;
}

/** yyyy-MM-dd du jour, dans le fuseau du navigateur. */
export const todayIso = () => toIso(new Date());

/** « 12,50 » ou « 12.50 » → 12.5 ; vide → null ; autre chose → 'invalid'. La virgule est convertie avant envoi. */
export const parseAmount = (raw: string): number | null | 'invalid' => {
  const cleaned = raw.replace(/\s/g, '').replace(',', '.');
  if (cleaned === '') return null;
  if (!/^\d+(\.\d+)?$/.test(cleaned)) return 'invalid';
  return Number(cleaned);
};

/** Un montant relu du serveur, écrit avec la virgule pour la saisie. */
export const amountToInput = (amount: number | null) => (amount == null ? '' : String(amount).replace('.', ','));

/**
 * Les erreurs du serveur, champ par champ. Un 400 de validation ASP.NET porte `errors: { Label: [...] }`,
 * un 400 ou 409 métier porte du texte brut : il va en ligne générale, tel quel.
 */
export const fieldErrorsOf = (err: unknown, fallback: string): FieldErrors => {
  if (!isAxiosError(err)) return { general: fallback };
  const data: unknown = err.response?.data;
  if (typeof data === 'string' && data.trim()) return { general: data };
  if (data && typeof data === 'object' && 'errors' in data && data.errors && typeof data.errors === 'object') {
    const out: FieldErrors = {};
    const general: string[] = [];
    for (const [key, value] of Object.entries(data.errors as Record<string, unknown>)) {
      const message = Array.isArray(value) ? value.map(String).join(' ') : String(value);
      switch (key.toLowerCase()) {
        case 'label': out.label = message; break;
        case 'duedate': out.dueDate = message; break;
        case 'amount': out.amount = message; break;
        default: general.push(message);
      }
    }
    if (general.length) out.general = general.join(' ');
    return Object.keys(out).length ? out : { general: fallback };
  }
  return { general: fallback };
};
