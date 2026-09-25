import { useEffect, useMemo, useState } from 'react'
import { api, ApiError, Permission, type AboutTesria, type DependencyCheck } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { TESRIA_SITE, TESRIA_SOURCE, TESRIA_SUPPORT } from '../../links'

/**
 * Administration → About (2026-09-24): which Tesria this is, a way
 * to support it, everything it is built from with each license, and a check
 * of all of it against OSV.dev for known vulnerabilities. The check is only
 * ever made when someone presses the button, because it sends the package
 * names and versions to an outside service.
 */
const dateTime = (iso: string) =>
  new Date(iso).toLocaleString(undefined, { month: 'long', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit' })

export function AdminAboutPage() {
  const { can } = useAuth()
  const canCheck = can(Permission.SecurityView)
  const [about, setAbout] = useState<AboutTesria | null>(null)
  const [check, setCheck] = useState<DependencyCheck | null>(null)
  const [checking, setChecking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState('')
  const [component, setComponent] = useState('all')

  useEffect(() => {
    api.admin.about.get()
      .then((a) => { setAbout(a); setCheck(a.lastCheck) })
      .catch(() => setError('Could not load this page.'))
  }, [])

  async function runCheck() {
    setChecking(true)
    setError(null)
    try {
      setCheck(await api.admin.about.check())
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'The check could not be made.')
    } finally {
      setChecking(false)
    }
  }

  // Two lists (2026-09-24): the container images on their own,
  // since those are what someone checks with Docker's tools, and the
  // packages, which the button checks.
  const images = useMemo(() => (about?.dependencies ?? []).filter((d) => d.ecosystem === 'Container'), [about])
  const packageList = useMemo(() => (about?.dependencies ?? []).filter((d) => d.ecosystem !== 'Container'), [about])

  const components = useMemo(() => {
    const counts = new Map<string, number>()
    for (const d of packageList) counts.set(d.component, (counts.get(d.component) ?? 0) + 1)
    return [...counts.entries()]
  }, [packageList])

  const shown = useMemo(() => {
    const q = filter.trim().toLowerCase()
    return packageList.filter((d) =>
      (component === 'all' || d.component === component)
      && (!q || d.name.toLowerCase().includes(q) || d.license.toLowerCase().includes(q)))
  }, [packageList, filter, component])

  if (!about) return error ? <p className="alert alert--error">{error}</p> : <p className="muted">Loading…</p>

  // Each package once, however many parts of Tesria use it: what the check asks about.
  const packages = new Set(packageList.map((d) => `${d.ecosystem}:${d.name}@${d.version}`)).size
  return (
    <>
      <section className="about-card">
        <h2>Tesria {about.version}</h2>
        <p>
          Tesria is a self-hosted wiki: spaces of pages your team writes together, with history,
          comments, search, exports and backups, on a server you control. It is free and open source,
          under the Apache License 2.0.
        </p>
        {about.previousVersion && about.versionChangedAt && (
          <p className="muted small">
            Upgraded from {about.previousVersion} on {dateTime(about.versionChangedAt)}.
          </p>
        )}
        <p className="about-card__links">
          <a href={TESRIA_SITE} target="_blank" rel="noopener noreferrer">tesria.com</a>
          <a href={TESRIA_SOURCE} target="_blank" rel="noopener noreferrer">Source code</a>
          <a href="/api/admin/about/notices" target="_blank" rel="noopener noreferrer">Third-party licenses</a>
        </p>
      </section>

      <section className="about-card about-card--support">
        <h2>Support Tesria</h2>
        <p>
          Tesria is completely free. It exists because I wanted a great wiki to be available to the
          open-source community. If it is useful to you and you would like to buy me a coffee or a beer,
          or just show your appreciation, you can support it through GitHub Sponsors or Ko-fi. Thank you.
        </p>
        <p className="muted small">Brian, who makes Tesria</p>
        <a className="btn btn--primary" href={TESRIA_SUPPORT} target="_blank" rel="noopener noreferrer">
          Ways to support Tesria
        </a>
      </section>

      <section className="about-card">
        <h2>Known vulnerabilities</h2>
        <p className="muted small">
          When a new vulnerability is announced, check here whether this Tesria is affected. The check asks
          OSV.dev, the open vulnerability database behind GitHub’s and npm’s security advisories, about
          each of the {packages} packages below. It sends their names and versions, and this server’s
          address, and nothing else. It only happens when someone chooses the button.
        </p>
        {error && <p className="alert alert--error">{error}</p>}
        {check && (check.affected.length === 0 ? (
          <p className="alert alert--success">
            No known vulnerabilities in the {check.checked} packages, as of {dateTime(check.at)}
            {check.byName ? `, checked by ${check.byName}` : ''}.
          </p>
        ) : (
          <>
            <p className="alert alert--error">
              {check.affected.length} {check.affected.length === 1 ? 'package has' : 'packages have'} known
              vulnerabilities, as of {dateTime(check.at)}{check.byName ? `, checked by ${check.byName}` : ''}.
              Update Tesria when a release fixes them; each advisory says which version of the package does.
            </p>
            <ul className="about-vulns">
              {check.affected.map((a) => (
                <li key={`${a.ecosystem}:${a.name}@${a.version}`}>
                  <strong>{a.name}</strong> {a.version} <span className="muted small">({a.components.join(', ')})</span>
                  <ul>
                    {a.vulnerabilities.map((v) => (
                      <li key={v.id}>
                        <a href={v.url} target="_blank" rel="noopener noreferrer">{v.id}</a>
                        {v.aliases.filter((x) => x.startsWith('CVE-')).map((x) => <span key={x} className="muted small"> {x}</span>)}
                        {v.severity && <> <span className={`badge${/crit|high/i.test(v.severity) ? ' badge--danger' : ' badge--warning'}`}>{v.severity.toLowerCase()}</span></>}
                        {v.summary && <div className="small">{v.summary}</div>}
                      </li>
                    ))}
                  </ul>
                </li>
              ))}
            </ul>
          </>
        ))}
        {!check && <p className="muted">Not checked yet.</p>}
        {canCheck && (
          <button type="button" className="btn" disabled={checking} onClick={runCheck}>
            {checking ? 'Checking…' : 'Check for known vulnerabilities'}
          </button>
        )}
        <p className="muted small">
          This covers the packages below. The container images Tesria runs on are checked with Docker’s
          own tools: see <a href="#container-images">Container images</a>.
        </p>
      </section>

      <h2 className="dash__heading" id="container-images">Container images</h2>
      <p className="muted small">
        The operating systems and runtimes Tesria runs on, one image each. OSV.dev does not cover them.
        To check one for known vulnerabilities, run its command on the server (Docker Scout comes with
        Docker Desktop; <code>trivy image</code> works the same way). To pick up the images’ own
        security updates, run <code>docker compose pull</code> and <code>docker compose build --pull</code>,
        then <code>docker compose up -d</code>.
      </p>
      <ul className="about-images">
        {images.map((d) => {
          const ref = `${d.name}:${d.version}`
          const command = `docker scout cves ${ref}`
          return (
            <li key={ref}>
              <code className="about-images__ref">{ref}</code>
              {d.note && <span className="muted small"> {d.note}</span>}
              <div className="about-images__command">
                <code>{command}</code>
                <button type="button" className="btn btn--ghost btn--sm"
                  onClick={() => navigator.clipboard.writeText(command).catch(() => {})}>Copy</button>
              </div>
            </li>
          )
        })}
      </ul>

      <h2 className="dash__heading">Packages</h2>
      <p className="muted small">
        Everything else this version of Tesria ships, with its license: {components.map(([c, n], i) => (
          <span key={c}>{i > 0 ? ', ' : ''}{n} in the {c === 'PDF service' ? c : c.toLowerCase()}</span>
        ))}. The vulnerability check above covers all of them.
      </p>
      <div className="row-gap about-filter">
        <input type="search" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Filter by package or license" aria-label="Filter by package or license" />
        <select value={component} onChange={(e) => setComponent(e.target.value)} aria-label="Part of Tesria">
          <option value="all">All parts</option>
          {components.map(([c]) => <option key={c} value={c}>{c}</option>)}
        </select>
      </div>
      <table className="admin-table">
        <thead>
          <tr><th>Package</th><th>Version</th><th>License</th><th>Part</th></tr>
        </thead>
        <tbody>
          {shown.map((d) => (
            <tr key={`${d.component}|${d.ecosystem}|${d.name}|${d.version}`}>
              <td>
                {d.url ? <a href={d.url} target="_blank" rel="noopener noreferrer">{d.name}</a> : d.name}
                {d.direct && <span className="muted small"> direct</span>}
              </td>
              <td className="small">{d.version}</td>
              <td className="small">{d.license}</td>
              <td className="small muted">{d.component}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  )
}
