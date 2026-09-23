// Copie dans le presse-papiers avec repli. La prod tourne en http://raspberrypi5:5001, contexte non sécurisé :
// navigator.clipboard y est undefined, seul document.execCommand('copy') fonctionne encore.

/** Repli : un textarea hors écran, sélectionné, puis execCommand. Vrai si le navigateur dit avoir copié. */
const copyViaExecCommand = (text: string): boolean => {
  // Le focus part sur le textarea le temps de la copie : on le rend ensuite au bouton qui l'avait.
  const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  const textarea = document.createElement('textarea');
  textarea.value = text;
  textarea.setAttribute('readonly', '');
  textarea.setAttribute('aria-hidden', 'true');
  // Fixe en haut et hors écran : pas de défilement au focus, invisible.
  textarea.style.position = 'fixed';
  textarea.style.top = '0';
  textarea.style.left = '-9999px';
  textarea.style.opacity = '0';
  document.body.appendChild(textarea);
  textarea.focus();
  textarea.select();
  // iOS ignore select() sur un textarea readonly sans plage explicite.
  textarea.setSelectionRange(0, text.length);
  let copied = false;
  try {
    copied = document.execCommand('copy');
  } catch (err) {
    // Navigateur qui refuse la commande : on rend faux, l'écran propose la sélection à la main.
    console.warn("execCommand('copy') a échoué", err);
    copied = false;
  } finally {
    textarea.remove();
    previous?.focus();
  }
  return copied;
};

/**
 * Met `text` dans le presse-papiers. L'API asynchrone quand elle existe (contexte sécurisé), sinon le repli
 * execCommand. Faux quand les deux échouent : l'appelant doit alors rendre la valeur sélectionnable.
 */
export const copyText = async (text: string): Promise<boolean> => {
  if (typeof navigator !== 'undefined' && navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch (err) {
      // Permission refusée ou document sans focus : le repli a encore sa chance.
      console.warn('navigator.clipboard.writeText a échoué, repli execCommand', err);
    }
  }
  return copyViaExecCommand(text);
};
