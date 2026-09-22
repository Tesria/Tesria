import { createContext, useContext, useEffect } from 'react'
import type { PageTreeNode, Space } from '../api/client'

/**
 * What the current space's navigation looks like, published upward from
 * `SpacePage` so the app shell can show it somewhere `SpacePage` cannot
 * reach: the phone's hamburger menu.
 *
 * On a desktop the sidebar is always there and the tree is one click away.
 * On a phone the sidebar is gone, so changing page meant hamburger → Spaces →
 * the space → the page: three taps to move between two pages of the same
 * space. The shell renders the tree in the menu instead, but the tree is
 * loaded by the space route, which is a child of the shell, hence a
 * context flowing the "wrong" way, from route to shell. It carries only
 * what the menu needs.
 */
export type SpaceNav = {
  space: Space
  tree: PageTreeNode[]
  /** Where "+ New page" goes: under the open page when there is one. */
  newPageHref: string
}

export type SpaceNavContextValue = {
  nav: SpaceNav | null
  setNav: (nav: SpaceNav | null) => void
}

export const SpaceNavContext = createContext<SpaceNavContextValue>({
  nav: null,
  setNav: () => {},
})

/** The shell reads this to decide whether the menu has a space section. */
export function useSpaceNav(): SpaceNav | null {
  return useContext(SpaceNavContext).nav
}

/**
 * The space route calls this to publish its tree, and to withdraw it when
 * the route unmounts, leaving a space must take its pages out of the menu.
 */
export function usePublishSpaceNav(nav: SpaceNav | null) {
  const { setNav } = useContext(SpaceNavContext)
  useEffect(() => {
    setNav(nav)
    return () => setNav(null)
  }, [nav, setNav])
}
