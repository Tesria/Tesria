/**
 * What a new password must be (T1-026). The server owns the rule
 * (`PasswordRules` in the API, which also refuses the ten thousand most
 * common passwords); this is the part a page can check before sending, and
 * the words every new-password field shows, so the fields and the server's
 * messages say the same thing.
 */
export const PASSWORD_MIN = 8
export const PASSWORD_MAX = 1024

export const PASSWORD_HINT = `At least ${PASSWORD_MIN} characters, not only spaces, and not a common password such as “password” or “12345678”.`

/** What is wrong with a new password that can be told without the server, or null. */
export function passwordProblem(password: string): string | null {
  if (password.length < PASSWORD_MIN) return `Password must be at least ${PASSWORD_MIN} characters.`
  if (password.length > PASSWORD_MAX) return `Password must be at most ${PASSWORD_MAX} characters.`
  if (password.trim() === '') return 'Password cannot be only spaces.'
  return null
}
