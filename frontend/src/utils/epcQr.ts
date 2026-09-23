// Charge utile d'un QR code de virement SEPA, format EPC069-12 version 002 (« BCD »). Fonction pure : la
// génération de l'image vit dans la fiche, en import dynamique de `qrcode`.
import { formatStructuredCommunication } from '../components/echeances/echeanceForm';

export interface EpcPayloadInput {
  /** Nom du bénéficiaire tel que sa banque le connaît, 70 caractères au plus (tronqué au-delà). */
  name: string;
  /** IBAN compact, majuscules, sans espace. */
  iban: string;
  /** Montant en euros. Null, zéro ou négatif : la ligne reste vide, l'app bancaire le demandera. */
  amount: number | null;
  /** Les douze chiffres de la communication structurée belge, ou null. */
  structuredCommunication: string | null;
  /** Le libellé de l'échéance, écrit en communication libre quand il n'y a pas de communication structurée. */
  label: string;
}

/**
 * Les onze lignes du format, séparées par `\n`, sans retour final : en-tête BCD, version 002, encodage 1
 * (UTF-8), SCT, BIC vide, nom, IBAN, montant « EUR12.34 » (point décimal, deux décimales) ou vide, purpose
 * vide, référence ISO 11649 vide (on n'utilise pas RF), puis la communication libre : la communication
 * structurée formatée « +++123/4567/89002+++ », que les banques belges lisent dans ce champ, sinon le
 * libellé tronqué à 140. Les lignes vides en fin de charge sont omises, jamais celles du milieu.
 */
export const buildEpcPayload = ({ name, iban, amount, structuredCommunication, label }: EpcPayloadInput): string => {
  const remittance = structuredCommunication
    ? formatStructuredCommunication(structuredCommunication)
    : label.slice(0, 140);
  const lines = [
    'BCD',
    '002',
    '1',
    'SCT',
    '',
    name.slice(0, 70),
    iban,
    amount != null && amount > 0 ? `EUR${amount.toFixed(2)}` : '',
    '',
    '',
    remittance,
  ];
  while (lines.length > 0 && lines[lines.length - 1] === '') lines.pop();
  return lines.join('\n');
};
