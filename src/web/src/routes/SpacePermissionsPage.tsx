import { AccessExplainer } from '../components/AccessExplainer'
import { SpaceAccessEditor } from '../components/SpaceAccessEditor'
import { useSpaceContext } from './SpacePage'

export function SpacePermissionsPage() {
  const { space } = useSpaceContext()
  return (
    <>
      <SpaceAccessEditor spaceKey={space.key} />
      {/* Why someone can see this space (dev-plan 21.4): for its administrators, who see its permissions. */}
      {space.canAdmin && <AccessExplainer space={space} />}
    </>
  )
}
