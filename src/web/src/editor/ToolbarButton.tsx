/** A single formatting toggle button, shared by the static toolbar and the selection bubble menu. */
export function ToolbarButton({
  label,
  isActive,
  onClick,
  title,
}: {
  label: string
  isActive: boolean
  onClick: () => void
  title: string
}) {
  return (
    <button
      type="button"
      className={isActive ? 'toolbar__btn is-active' : 'toolbar__btn'}
      onMouseDown={(e) => e.preventDefault()} // keep the editor selection
      onClick={onClick}
      title={title}
    >
      {label}
    </button>
  )
}
