import { SpaceAccessEditor } from '../components/SpaceAccessEditor'
import { useSpaceContext } from './SpacePage'

export function SpacePermissionsPage() {
  const { space } = useSpaceContext()
  return <SpaceAccessEditor spaceKey={space.key} />
}
