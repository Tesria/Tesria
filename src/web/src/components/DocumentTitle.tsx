import { createContext, use, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import { useInstance } from '../InstanceContext'
import { formatTitle, sectionFor } from '../title'

type Parts = { setSpace: (name: string | null) => void; setPage: (title: string | null) => void }

const TitleContext = createContext<Parts>({ setSpace: () => {}, setPage: () => {} })

/**
 * Keeps the browser tab's title as "Instance Name - Space Name / Page Name"
 * (dev-plan 13.1, decision 15). Until this, the app never set a title at all,
 * so every tab said "Tesria" whatever was open in it.
 *
 * The route decides the section; the space and page views say which space
 * and page they show, through the two hooks below, because only they know
 * the names. One place writes `document.title`, so there is never a race
 * between a parent's effect and a child's.
 */
export function DocumentTitleProvider({ children }: { children: ReactNode }) {
  const instance = useInstance()
  const { pathname } = useLocation()
  const [space, setSpace] = useState<string | null>(null)
  const [page, setPage] = useState<string | null>(null)

  useEffect(() => {
    // The server already wrote the right title into the page it sent; until
    // the instance answers, leave it alone rather than flash "Tesria".
    if (!instance) return
    document.title = formatTitle(instance.instanceName, { space, page, section: sectionFor(pathname) })
  }, [instance, pathname, space, page])

  const parts = useMemo(() => ({ setSpace, setPage }), [])
  return <TitleContext value={parts}>{children}</TitleContext>
}

/** The open space's name, for the tab title. Cleared when the space view goes. */
export function useTitleSpace(name: string | null | undefined) {
  const { setSpace } = use(TitleContext)
  useEffect(() => {
    setSpace(name ?? null)
    return () => setSpace(null)
  }, [name, setSpace])
}

/** The open page's title. Null while it loads, which shows the space alone. */
export function useTitlePage(title: string | null | undefined) {
  const { setPage } = use(TitleContext)
  useEffect(() => {
    setPage(title ?? null)
    return () => setPage(null)
  }, [title, setPage])
}
