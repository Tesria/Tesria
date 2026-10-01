// Export as PDF (dev-plan 8.1), saying so while the PDF is made. A plain link
// gave nothing to see for the seconds the sidecar takes, so this fetches the
// file itself and saves it when it arrives. Going elsewhere in Tesria
// meanwhile does not stop it; closing the tab does.
import { type ReactNode, useState } from 'react'
import { DownloadIcon } from './NavIcons'
import { downloadName } from './downloadName'

export function PdfExportButton({ pageId, className, children }: { pageId: string; className: string; children: ReactNode }) {
  const [preparing, setPreparing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function run() {
    setPreparing(true)
    setError(null)
    try {
      const res = await fetch(`/api/pages/${pageId}/export?format=pdf`, { credentials: 'include' })
      if (!res.ok) {
        const body = (await res.json().catch(() => null)) as { detail?: string; message?: string } | null
        throw new Error(body?.detail ?? body?.message ?? 'The PDF could not be made.')
      }
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = downloadName(res.headers.get('Content-Disposition')) ?? 'page.pdf'
      document.body.append(link)
      link.click()
      link.remove()
      window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    } catch (err) {
      setError(err instanceof Error && err.message !== 'Failed to fetch' ? err.message : 'The PDF could not be made.')
    } finally {
      setPreparing(false)
    }
  }

  return (
    <>
      <button type="button" className={className} disabled={preparing} aria-busy={preparing} onClick={() => void run()}>
        <DownloadIcon /> {preparing ? 'Preparing PDF…' : children}
      </button>
      {error && <span className="small export-pdf__error" role="alert">{error}</span>}
    </>
  )
}
