import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { echeancesApi } from '../../api/echeances';
import { useEcheanceQuery } from '../../hooks/queries';
import type { AgendaItem, AgendaStatus } from '../../types/agenda';
import { copyText } from '../../utils/clipboard';
import { buildEpcPayload } from '../../utils/epcQr';
import { formatCurrency } from '../../utils/format';
import { formatInstantDay, formatLongDate, formatShortDate, statusLabel } from './agendaFormat';
import { EcheanceDocumentsSection } from '../echeances/EcheanceDocumentsSection';
import { EcheanceEditDelete } from '../echeances/EcheanceEditDelete';
import { EcheanceFormSheet } from '../echeances/EcheanceFormSheet';
import { formatIban, formatStructuredCommunication } from '../echeances/echeanceForm';

interface Props {
  echeanceId: number;
  /** La ligne telle que l'agenda la montre, rafraîchie après chaque geste. Absente si elle a quitté l'écran. */
  item: AgendaItem | undefined;
  dashboardId: number;
  onClose: () => void;
}

/** Le texte du serveur quand il en donne un (400, 409), sinon une phrase neutre. */
const messageOf = (err: unknown, fallback: string) => {
  if (isAxiosError(err) && typeof err.response?.data === 'string' && err.response.data.trim()) return err.response.data;
  return fallback;
};

/** Le statut de l'EcheanceDto (AVenir, EnRetard, Payee) ramené aux mots de l'agenda, sans rien recalculer. */
const statusFromDto = (status: string | undefined): AgendaStatus | null => {
  switch (status) {
    case 'Payee':
      return 'paid';
    case 'EnRetard':
      return 'late';
    case 'AVenir':
      return 'due';
    default:
      return null;
  }
};

type PayKey = 'iban' | 'communication' | 'amount';

interface PayRow {
  key: PayKey;
  label: string;
  /** Formaté comme le reste de la fiche. */
  shown: string;
  /** Ce qui part dans le presse-papiers : IBAN compact, communication avec les +++, montant « 2,60 ». */
  copy: string;
  /** Nom accessible du bouton, stable pendant le retour « Copié ». */
  action: string;
  /** Annonce de la zone aria-live après la copie. */
  done: string;
}

/** « 2,60 » : virgule, deux décimales, sans symbole, tel qu'une app bancaire l'accepte à la saisie. */
const amountForCopy = (amount: number) => amount.toFixed(2).replace('.', ',');

/**
 * Feuille basse sur le patron de CategoryDetailModal. Titre, montant, date limite, statut en texte, puis
 * les gestes : « Je l'ai payée », réversible au même endroit par « Finalement non », qui couvre aussi le
 * cas d'une transaction liée par le rapprocheur : c'est le seul geste qui détache, et le serveur ne redevine
 * plus tant qu'une clé ne change pas. Chaque geste
 * invalide l'agenda, le serveur recalcule le statut. Lot 1 : une section Documents, puis « Modifier » (la
 * feuille de saisie prend la place de celle-ci, et ne touche jamais au lien de paiement) et « Supprimer ».
 * Lot 3 : quand c'est le rapprocheur qui a lié la transaction (matchedAt), le statut dit « Vu sur le compte »
 * et montre le virement ; les clés saisies (IBAN, communication) s'affichent formatées quand elles existent.
 * Fiche « prête à payer » (23/09) : tant que l'échéance est à payer, une section Payer copie chaque clé en un
 * geste (repli execCommand pour la prod en HTTP) et montre un QR code EPC quand l'IBAN et le bénéficiaire sont
 * là. L'app n'initie jamais le virement : elle le prépare, le rapprocheur constate ensuite qu'il est passé.
 */
export const EcheanceSheet = ({ echeanceId, item, dashboardId, onClose }: Props) => {
  const queryClient = useQueryClient();
  const { data: echeance, isLoading } = useEcheanceQuery(echeanceId);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);

  // Pendant la modification, la feuille de saisie tient Échap : sinon les deux se fermeraient d'un coup.
  useEffect(() => {
    if (editing) return;
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onEsc);
    return () => document.removeEventListener('keydown', onEsc);
  }, [onClose, editing]);

  const invalidate = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['agenda', dashboardId] }),
      queryClient.invalidateQueries({ queryKey: ['echeance', echeanceId] }),
      // La page Documents lit le statut dans la liste : sans ça, la carte garde « à payer » après le geste.
      queryClient.invalidateQueries({ queryKey: ['echeances', dashboardId] }),
    ]);
  };

  const pay = useMutation({
    mutationFn: () => echeancesApi.pay(echeanceId),
    onSuccess: () => { setError(null); return invalidate(); },
    // 409 « déjà payée » : l'écran était en retard sur le serveur, on se remet à jour.
    onError: (err) => { setError(messageOf(err, "Impossible d'enregistrer le paiement, réessaie.")); return invalidate(); },
  });

  const unpay = useMutation({
    mutationFn: () => echeancesApi.unpay(echeanceId),
    onSuccess: () => { setError(null); return invalidate(); },
    onError: (err) => setError(messageOf(err, "Impossible d'annuler le paiement, réessaie.")),
  });

  const busy = pay.isPending || unpay.isPending;
  const status: AgendaStatus | null = item?.status ?? statusFromDto(echeance?.status);
  const title = item?.title ?? echeance?.label ?? '';
  const amount = item ? item.amount : echeance?.amount ?? null;
  const dueDate = echeance?.dueDate ?? (item?.originalDate ?? item?.date);
  const transactionId = echeance?.transactionId ?? item?.transactionId ?? null;

  // Section Payer : à payer ou en retard, et au moins une clé à copier. Une échéance payée ne la montre pas.
  const payIban = echeance?.counterpartyIban ?? null;
  const payCommunication = echeance?.structuredCommunication ?? null;
  const payAmount = echeance?.amount ?? null;
  const payName = echeance?.counterpartyName ?? null;
  const payable = (status === 'due' || status === 'late') && (payIban != null || payCommunication != null || payAmount != null);
  const payRows: PayRow[] = payable
    ? [
        ...(payIban ? [{ key: 'iban' as const, label: 'IBAN', shown: formatIban(payIban), copy: payIban, action: "Copier l'IBAN", done: 'IBAN copié' }] : []),
        ...(payCommunication
          ? [{ key: 'communication' as const, label: 'Communication', shown: formatStructuredCommunication(payCommunication), copy: formatStructuredCommunication(payCommunication), action: 'Copier la communication', done: 'Communication copiée' }]
          : []),
        ...(payAmount != null ? [{ key: 'amount' as const, label: 'Montant', shown: formatCurrency(payAmount), copy: amountForCopy(payAmount), action: 'Copier le montant', done: 'Montant copié' }] : []),
      ]
    : [];

  const [copied, setCopied] = useState<PayKey | null>(null);
  const [copyFailed, setCopyFailed] = useState<PayKey | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const copiedTimer = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(copiedTimer.current), []);

  const copyRow = async (row: PayRow) => {
    const ok = await copyText(row.copy);
    if (!ok) {
      setCopied(null);
      setCopyFailed(row.key);
      setAnnouncement('Copie impossible, sélectionne le texte');
      return;
    }
    setCopyFailed(null);
    setCopied(row.key);
    setAnnouncement(row.done);
    window.clearTimeout(copiedTimer.current);
    copiedTimer.current = window.setTimeout(() => setCopied(null), 2000);
  };

  // QR code EPC : IBAN et bénéficiaire nécessaires, montant et communication quand ils existent. La charge
  // utile est une chaîne : l'effet ne repart que si elle change. `qrcode` s'importe à la demande, l'Agenda
  // ne le porte pas dans son bundle.
  const epcPayload = payable && payIban && payName
    ? buildEpcPayload({ name: payName, iban: payIban, amount: payAmount, structuredCommunication: payCommunication, label: echeance?.label ?? '' })
    : null;
  const [qr, setQr] = useState<{ payload: string; dataUrl: string } | null>(null);
  const [qrError, setQrError] = useState(false);
  useEffect(() => {
    if (!epcPayload) return;
    let cancelled = false;
    import('qrcode')
      // Fond blanc et marge de quatre modules : l'app est sombre, un QR sur fond sombre ne se scanne pas.
      .then((QRCode) => QRCode.toDataURL(epcPayload, { errorCorrectionLevel: 'M', width: 200, margin: 4, color: { dark: '#000000', light: '#ffffff' } }))
      .then((dataUrl) => {
        if (cancelled) return;
        setQr({ payload: epcPayload, dataUrl });
        setQrError(false);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        // Charge utile hors capacité ou module absent : on le dit à l'écran, la charge utile ne passe pas dans les journaux.
        console.warn('Génération du QR code de virement impossible', err instanceof Error ? err.message : err);
        setQrError(true);
      });
    return () => { cancelled = true; };
  }, [epcPayload]);
  const qrReady = epcPayload && qr && qr.payload === epcPayload ? qr : null;

  const matched = !!echeance?.matchedAt && transactionId != null;
  const statusText = (() => {
    if (status === 'paid') {
      if (echeance?.paidAt) return `Payée le ${formatInstantDay(echeance.paidAt)}`;
      // Le rapprocheur a lié le virement : on dit quand il est passé sur le compte, pas quand on l'a vu.
      if (matched) return `Vu sur le compte le ${echeance.payment ? formatShortDate(echeance.payment.date) : formatInstantDay(echeance.matchedAt!)}`;
      return transactionId != null ? 'Payée, réglée par une transaction' : 'Payée';
    }
    if (item) {
      const label = statusLabel(item);
      return label ? label.charAt(0).toUpperCase() + label.slice(1) : '';
    }
    if (status === 'late') return 'En retard';
    if (status === 'due') return 'À payer';
    return '';
  })();

  if (editing && echeance) {
    return <EcheanceFormSheet dashboardId={dashboardId} initial={echeance} onClose={() => setEditing(false)} />;
  }

  const sheet = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="echeance-sheet-title"
      className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-end md:items-center justify-center"
      onClick={onClose}
    >
      <div
        className="bg-[#1a1a3e] rounded-t-2xl md:rounded-2xl border border-white/10 p-6 pb-[max(1.5rem,env(safe-area-inset-bottom))] w-full md:max-w-md max-h-[85vh] overflow-y-auto"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3 mb-4">
          <div className="min-w-0">
            <h3 id="echeance-sheet-title" className="text-xl font-bold text-white break-words">{title || 'Échéance'}</h3>
            <p className="text-white/70 text-lg font-semibold tabular-nums mt-1">
              {amount != null ? formatCurrency(amount) : <span className="text-white/40 text-sm font-normal">Montant inconnu</span>}
            </p>
          </div>
          <button onClick={onClose} aria-label="Fermer" className="text-white/40 hover:text-white text-2xl leading-none px-2 min-h-11">
            ×
          </button>
        </div>

        <dl className="text-sm space-y-1 mb-5">
          <div className="flex gap-2">
            <dt className="text-white/40 shrink-0">Date limite</dt>
            <dd className="text-white/80">{dueDate ? formatLongDate(dueDate) : (isLoading ? '…' : '')}</dd>
          </div>
          <div className="flex gap-2">
            <dt className="text-white/40 shrink-0">Statut</dt>
            <dd className={`min-w-0 ${status === 'late' ? 'text-red-400' : status === 'paid' ? 'text-emerald-400' : 'text-white/80'}`}>
              {statusText || (isLoading ? '…' : '')}
              {matched && echeance.payment && (
                <span className="block text-white/50 truncate">
                  {[echeance.payment.description, echeance.payment.counterpartyName].filter((s) => s && s.trim()).join(' · ')}
                </span>
              )}
            </dd>
          </div>
          {/* Quand la section Payer est là, c'est elle qui porte l'IBAN et la communication, avec le bouton Copier. */}
          {!payable && echeance?.counterpartyIban && (
            <div className="flex gap-2">
              <dt className="text-white/40 shrink-0">IBAN</dt>
              <dd className="text-white/80 tabular-nums">{formatIban(echeance.counterpartyIban)}</dd>
            </div>
          )}
          {!payable && echeance?.structuredCommunication && (
            <div className="flex gap-2">
              <dt className="text-white/40 shrink-0">Communication</dt>
              <dd className="text-white/80 tabular-nums">{formatStructuredCommunication(echeance.structuredCommunication)}</dd>
            </div>
          )}
        </dl>

        {payable && (
          <section aria-labelledby="echeance-pay-title" className="mb-5 rounded-xl border border-white/10 bg-white/5 p-4">
            <h4 id="echeance-pay-title" className="text-sm font-semibold text-white mb-3">Payer</h4>
            <ul className="space-y-2 text-sm">
              {payRows.map((row) => (
                <li key={row.key} className="flex flex-wrap items-center gap-x-2 gap-y-1">
                  {/* Sur téléphone la valeur prend sa propre ligne sous le libellé : un IBAN coupé en plein groupe
                      (« 0754 7 / 034 ») ne se recopie pas. À partir de sm, libellé, valeur et bouton sur une ligne.
                      L'ordre du DOM reste libellé, valeur, bouton pour la lecture d'écran, l'ordre visuel suit `order`. */}
                  <span className="order-1 text-white/40 shrink-0 w-28">{row.label}</span>
                  <span className={`order-3 basis-full sm:order-2 sm:basis-0 sm:flex-1 min-w-0 text-white/80 tabular-nums break-words ${copyFailed === row.key ? 'select-all' : ''}`}>{row.shown}</span>
                  <button
                    type="button"
                    aria-label={row.action}
                    onClick={() => copyRow(row)}
                    className="order-2 ml-auto sm:order-3 sm:ml-0 shrink-0 min-h-9 px-3 rounded-lg border border-white/10 text-xs text-white/70 hover:text-white hover:bg-white/5 transition-colors"
                  >
                    {copied === row.key ? 'Copié' : 'Copier'}
                  </button>
                  {/* Sous la ligne, pas dans le bouton : à 360 px le bouton écraserait la valeur qu'on demande de sélectionner. */}
                  {copyFailed === row.key && (
                    <span className="order-4 basis-full text-xs text-amber-300/90">Copie impossible, sélectionne le texte</span>
                  )}
                </li>
              ))}
            </ul>
            <p aria-live="polite" className="sr-only">{announcement}</p>

            {qrReady && (
              <figure className="mt-4 flex flex-col items-center">
                <img
                  src={qrReady.dataUrl}
                  alt="QR code de virement"
                  data-epc={qrReady.payload}
                  width={200}
                  height={200}
                  className="rounded-lg bg-white p-1"
                />
                <figcaption className="mt-2 text-xs text-white/50">Scanne avec ton app bancaire</figcaption>
              </figure>
            )}
            {epcPayload && !qrReady && (
              <p className="mt-4 text-xs text-white/50 text-center">{qrError ? 'QR code indisponible, copie les champs.' : 'QR code en préparation…'}</p>
            )}
            {payIban && !payName && (
              <p className="mt-4 text-xs text-white/50">Renseigne le bénéficiaire pour obtenir un QR code</p>
            )}
          </section>
        )}

        {error && <p className="text-xs text-amber-300/90 mb-3">{error}</p>}

        <div className="space-y-2">
          {(status === 'due' || status === 'late') && (
            <button
              type="button"
              onClick={() => pay.mutate()}
              disabled={busy}
              className="w-full min-h-11 rounded-xl bg-amber-500/20 text-amber-300 border border-amber-500/30 font-medium hover:bg-amber-500/30 disabled:opacity-50 transition-colors"
            >
              {pay.isPending ? 'Enregistrement…' : "Je l'ai payée"}
            </button>
          )}

          {status === 'paid' && (
            <button
              type="button"
              onClick={() => unpay.mutate()}
              disabled={busy}
              className="w-full min-h-11 rounded-xl border border-white/10 text-white/70 hover:text-white hover:bg-white/5 disabled:opacity-50 transition-colors"
            >
              {unpay.isPending ? 'Annulation…' : 'Finalement non'}
            </button>
          )}
        </div>

        <EcheanceDocumentsSection echeanceId={echeanceId} dashboardId={dashboardId} />

        {echeance && (
          <EcheanceEditDelete echeanceId={echeanceId} dashboardId={dashboardId} onEdit={() => setEditing(true)} onDeleted={onClose} />
        )}
      </div>
    </div>
  );

  return createPortal(sheet, document.body);
};
