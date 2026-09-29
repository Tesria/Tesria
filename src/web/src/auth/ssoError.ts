/**
 * The sign-in page's messages for a single sign-on refusal. The server sends
 * only a short code (`/login?ssoError=invite_only`), never text, and the
 * page shows only the message it holds for that code: a link must not be
 * able to put words of its choosing in the sign-in page's error box, and a
 * person's email address does not belong in a URL. Keep the codes in step
 * with `SsoErrors` on the server.
 */
const MESSAGES = new Map<string, string>(Object.entries({
  invite_only:
    'There is no account for your email address, and this instance is invite only. ' +
    'Ask an administrator for an invite, create your account from it, then sign in with single sign-on.',
  email_not_verified:
    'An account already exists for your email address, but your identity provider did not confirm ' +
    'that the address is verified, so the two cannot be connected. Sign in with your password, ' +
    'or verify the address with your identity provider, then try again.',
  no_email:
    'Your identity provider did not share an email address, which is needed to sign you in. ' +
    'Ask your administrator to check the provider’s settings.',
  failed: 'Single sign-on did not finish. Try again, or sign in with your password.',
}))

/** The message for a code, or null for no code or one this page does not know. */
export function ssoErrorMessage(code: string | null | undefined): string | null {
  if (!code) return null
  return MESSAGES.get(code) ?? null
}
