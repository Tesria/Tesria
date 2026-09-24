import { useState } from 'react'

/**
 * Shows a freshly-issued set of recovery codes.
 *
 * This is the only moment the codes exist in readable form, the server keeps
 * hashes, so the component is built around getting them somewhere safe before
 * they are dismissed: a copy button, a download, and an explicit confirmation
 * rather than a close button that could be clicked past by reflex.
 */
export function RecoveryCodes({
  codes,
  onDone,
  doneLabel = "I've saved these",
}: {
  codes: string[]
  onDone?: () => void
  doneLabel?: string
}) {
  const [confirmed, setConfirmed] = useState(false)
  const [copied, setCopied] = useState(false)

  const asText = [
    'Tesria recovery codes',
    '',
    'Each code can be used once: to sign in in place of your authenticator app, or to reset your password if you are locked out.',
    'Keep them somewhere safe and offline.',
    '',
    ...codes,
    '',
  ].join('\n')

  function download() {
    const blob = new Blob([asText], { type: 'text/plain' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = 'tesria-recovery-codes.txt'
    link.click()
    URL.revokeObjectURL(url)
  }

  async function copy() {
    try {
      await navigator.clipboard.writeText(codes.join('\n'))
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard access can be refused (permissions, insecure context). The
      // codes are on screen and downloadable, so this is not worth an error.
    }
  }

  return (
    <div className="recovery">
      <p className="recovery__lead">
        Save these recovery codes. Each one can be used <strong>once</strong>, to
        sign in in place of your authenticator app or to reset your password if
        you are locked out, and this is the only time they will be shown.
      </p>

      <ol className="recovery__codes">
        {codes.map((code) => (
          <li key={code}>
            <code>{code}</code>
          </li>
        ))}
      </ol>

      <div className="row-gap">
        <button type="button" className="btn btn--ghost btn--sm" onClick={download}>
          Download
        </button>
        <button type="button" className="btn btn--ghost btn--sm" onClick={copy}>
          {copied ? 'Copied' : 'Copy'}
        </button>
      </div>

      {onDone && (
        <>
          <label className="recovery__confirm">
            <input
              type="checkbox"
              checked={confirmed}
              onChange={(e) => setConfirmed(e.target.checked)}
            />
            I have saved these codes somewhere safe
          </label>
          <button
            type="button"
            className="btn btn--primary"
            disabled={!confirmed}
            onClick={onDone}
          >
            {doneLabel}
          </button>
        </>
      )}
    </div>
  )
}
