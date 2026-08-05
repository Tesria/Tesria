import { useState, type ChangeEvent } from 'react'

/** Same stroke-icon language as the editor toolbar, topbar bell, and page
 *  tree pencil — flat, currentColor, 1.8px stroke. */
function EyeIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7-10-7-10-7Z" />
      <circle cx="12" cy="12" r="3" />
    </svg>
  )
}

function EyeOffIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M3 3l18 18" />
      <path d="M10.6 5.2A10.7 10.7 0 0 1 12 5c6.4 0 10 7 10 7a17.7 17.7 0 0 1-3.4 4.3M6.5 6.5C3.8 8.2 2 12 2 12s3.6 7 10 7a10.4 10.4 0 0 0 3.8-.7" />
      <path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" />
    </svg>
  )
}

/** A password `<input>` with a show/hide toggle — the one place the eye
 *  icon, its positioning, and its accessibility attributes need to be
 *  right, so every password field in the app (sign in, create account, and
 *  any future one) gets the same behavior for free. */
export function PasswordInput({
  value,
  onChange,
  autoComplete,
  required,
  minLength,
  autoFocus,
}: {
  value: string
  onChange: (e: ChangeEvent<HTMLInputElement>) => void
  autoComplete?: string
  required?: boolean
  minLength?: number
  autoFocus?: boolean
}) {
  const [visible, setVisible] = useState(false)

  return (
    <div className="password-input">
      <input
        type={visible ? 'text' : 'password'}
        value={value}
        onChange={onChange}
        autoComplete={autoComplete}
        required={required}
        minLength={minLength}
        autoFocus={autoFocus}
      />
      <button
        type="button"
        className="password-input__toggle"
        onClick={() => setVisible((v) => !v)}
        aria-label={visible ? 'Hide password' : 'Show password'}
        aria-pressed={visible}
      >
        {visible ? <EyeOffIcon /> : <EyeIcon />}
      </button>
    </div>
  )
}
