// Lecture des champs du formulaire d'échéance et des refus du serveur. Aucune règle métier : le serveur
// valide (longueur, plage), on ne fait que lire ce qu'il répond et l'écrire sous le bon champ.
import { isAxiosError } from 'axios';
import { toIso } from '../agenda/agendaFormat';

export interface FieldErrors {
  label?: string;
  dueDate?: string;
  amount?: string;
  counterpartyIban?: string;
  structuredCommunication?: string;
  general?: string;
}

/** L'exemple passe le contrôle 97 (1234567890 mod 97 = 2) : recopié tel quel, il est accepté. */
export const STRUCTURED_COMMUNICATION_HINT = 'Douze chiffres, par exemple +++123/4567/89002+++.';

/** Une saisie de communication structurée ramenée à ses chiffres : « +++123/4567/89012+++ » → « 123456789012 ». */
export const stripStructuredCommunication = (raw: string) => raw.replace(/[+*/\s]/g, '');

/**
 * Le contrôle belge : les deux derniers chiffres valent les dix premiers modulo 97, 0 donnant 97. Le même
 * calcul que le serveur, ici pour refuser avant l'envoi. Le serveur reste le juge.
 */
export const isValidStructuredCommunication = (twelveDigits: string) => {
  if (!/^\d{12}$/.test(twelveDigits)) return false;
  const expected = Number(twelveDigits.slice(0, 10)) % 97 || 97;
  return Number(twelveDigits.slice(10)) === expected;
};

/** Douze chiffres → « +++123/4567/89012+++ », pour l'affichage. Autre chose : tel quel. */
export const formatStructuredCommunication = (digits: string) =>
  digits.length === 12 ? `+++${digits.slice(0, 3)}/${digits.slice(3, 7)}/${digits.slice(7)}+++` : digits;

/** « BE98068243670693 » → « BE98 0682 4367 0693 », pour l'affichage et la relecture au formulaire. */
export const formatIban = (iban: string) => iban.replace(/\s+/g, '').toUpperCase().replace(/(.{4})(?=.)/g, '$1 ');

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
  if (typeof data === 'string' && data.trim()) {
    // Les deux refus métier du lot 3 arrivent en texte brut : ils vont sous leur champ, pas en ligne générale.
    if (data.startsWith('Communication structurée')) return { structuredCommunication: data };
    if (data.startsWith('IBAN')) return { counterpartyIban: data };
    return { general: data };
  }
  if (data && typeof data === 'object' && 'errors' in data && data.errors && typeof data.errors === 'object') {
    const out: FieldErrors = {};
    const general: string[] = [];
    for (const [key, value] of Object.entries(data.errors as Record<string, unknown>)) {
      const message = Array.isArray(value) ? value.map(String).join(' ') : String(value);
      switch (key.toLowerCase()) {
        case 'label': out.label = message; break;
        case 'duedate': out.dueDate = message; break;
        case 'amount': out.amount = message; break;
        case 'counterpartyiban': out.counterpartyIban = message; break;
        case 'structuredcommunication': out.structuredCommunication = message; break;
        default: general.push(message);
      }
    }
    if (general.length) out.general = general.join(' ');
    return Object.keys(out).length ? out : { general: fallback };
  }
  return { general: fallback };
};
