import { useEffect, useState } from 'react'
import './App.css'

type Health = {
  status: string
  service: string
  version: string
  utc: string
}

type Probe =
  | { state: 'loading' }
  | { state: 'ok'; data: Health }
  | { state: 'error'; message: string }

function App() {
  const [probe, setProbe] = useState<Probe>({ state: 'loading' })

  useEffect(() => {
    let cancelled = false
    fetch('/api/health')
      .then(async (r) => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`)
        return (await r.json()) as Health
      })
      .then((data) => {
        if (!cancelled) setProbe({ state: 'ok', data })
      })
      .catch((err: unknown) => {
        if (!cancelled)
          setProbe({
            state: 'error',
            message: err instanceof Error ? err.message : String(err),
          })
      })
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <main className="shell">
      <h1>ConfluenceClone</h1>
      <p className="tagline">Self-hosted knowledge base — Phase 1 foundation</p>

      <section className="card">
        <h2>API status</h2>
        {probe.state === 'loading' && <p>Checking…</p>}
        {probe.state === 'error' && <p className="bad">Unreachable: {probe.message}</p>}
        {probe.state === 'ok' && (
          <dl className="kv">
            <dt>Status</dt>
            <dd className="good">{probe.data.status}</dd>
            <dt>Service</dt>
            <dd>{probe.data.service}</dd>
            <dt>Version</dt>
            <dd>{probe.data.version}</dd>
            <dt>Server time (UTC)</dt>
            <dd>{new Date(probe.data.utc).toISOString()}</dd>
          </dl>
        )}
      </section>
    </main>
  )
}

export default App
