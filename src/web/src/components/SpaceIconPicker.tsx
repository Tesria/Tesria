import { type ChangeEvent, type FormEvent, useRef, useState } from 'react'
import { api, ApiError, type Space } from '../api/client'
import { SpaceIcon } from './SpaceIcon'
import { SPACE_ICON_COLORS, SpaceIconKind, spaceColorFor } from './spaceIconIdentity'

/**
 * A starting set, not a catalogue. A full emoji picker is a component with a
 * search index and a font-support matrix behind it; for choosing a space icon
 * once, a grid of likely ones plus a box to paste anything else is the whole
 * job. Every one here is single-codepoint from the U+1F300+ block, so it has
 * emoji presentation by default on every platform: no variation selectors,
 * nothing that renders as monochrome text on some systems.
 */
const SUGGESTED = [
  '🚀', '🎯', '🧭', '🔭', '🧪', '🧬', '🌍', '🌱',
  '🌟', '🔥', '💡', '📌', '📎', '📐', '📊', '📈',
  '📚', '📘', '📗', '📙', '📕', '📓', '📝', '🔑',
  '🔒', '🧰', '🧱', '🎨', '🎬', '🎵', '🎮', '🐉',
  '🦊', '🐙', '🦉', '🍀', '🧩', '🏆',
]

/** Longest edge sent to the server; it re-encodes to 256 either way. */
const UPLOAD_SIZE = 512

/**
 * Centre-crops to a square and scales down, so a phone photo is not uploaded
 * whole to produce a 24px tile. Identical in intent to the avatar picker's
 * crop, including `imageOrientation` so a portrait photo does not arrive
 * sideways: see AvatarPicker for the full reasoning.
 */
async function cropToSquare(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
  try {
    const edge = Math.min(bitmap.width, bitmap.height)
    const canvas = document.createElement('canvas')
    canvas.width = UPLOAD_SIZE
    canvas.height = UPLOAD_SIZE
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new Error('Canvas is unavailable in this browser.')
    ctx.imageSmoothingQuality = 'high'
    ctx.drawImage(bitmap, (bitmap.width - edge) / 2, (bitmap.height - edge) / 2, edge, edge, 0, 0, UPLOAD_SIZE, UPLOAD_SIZE)
    return await new Promise<Blob>((resolve, reject) => {
      canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Could not read that image.'))), 'image/png')
    })
  } finally {
    bitmap.close()
  }
}

/**
 * The icon section of a space's settings (dev-plan 6).
 *
 * Saves on every choice rather than behind a Save button: picking an emoji is
 * the whole interaction, and the preview above updates from the server's
 * answer, so what is shown is what is stored.
 */
export function SpaceIconPicker({ space, onChanged }: { space: Space; onChanged: (space: Space) => void }) {
  const fileInput = useRef<HTMLInputElement>(null)
  const [custom, setCustom] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const isImage = space.iconKind === SpaceIconKind.Image
  const activeColor = spaceColorFor(space)

  async function save(work: () => Promise<Space | void>, fallback: string) {
    setBusy(true)
    setError(null)
    try {
      const updated = await work()
      // The upload and remove routes answer with the media, not the space, so
      // re-read rather than guessing at the new state.
      onChanged(updated ?? (await api.spaces.get(space.key)))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : fallback)
    } finally {
      setBusy(false)
    }
  }

  const setIcon = (kind: SpaceIconKind, value: string | null, color?: number) =>
    save(
      () => api.spaces.update(space.key, {
        // The saved name, not whatever is half-typed in the field above:
        // choosing an icon must not commit an unfinished rename.
        name: space.name,
        description: space.description,
        iconKind: kind,
        iconValue: value,
        iconColor: color ?? space.iconColor,
      }),
      'Could not change the icon.',
    )

  async function onPickFile(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = '' // so re-picking the same file fires change again
    if (!file) return
    await save(async () => {
      await api.spaces.uploadIcon(space.key, await cropToSquare(file))
    }, 'Could not upload that image.')
  }

  function onCustom(e: FormEvent) {
    e.preventDefault()
    const value = custom.trim()
    if (!value) return
    setCustom('')
    return setIcon(SpaceIconKind.Emoji, value)
  }

  return (
    <div className="icon-picker">
      <div className="icon-picker__current">
        <SpaceIcon space={space} size={64} />
        <div>
          <p className="muted small">
            {isImage
              ? 'Your uploaded picture.'
              : space.iconKind === SpaceIconKind.Emoji
                ? 'An emoji on a coloured tile.'
                : `Generated from the key: the letter ${space.key[0]} on a tile.`}
          </p>
          <div className="row-gap">
            <button type="button" className="btn btn--ghost btn--sm" disabled={busy}
              onClick={() => fileInput.current?.click()}>
              {isImage ? 'Replace picture' : 'Upload picture'}
            </button>
            {space.iconKind !== SpaceIconKind.None && (
              <button type="button" className="btn btn--ghost btn--sm" disabled={busy}
                onClick={() => (isImage
                  ? save(() => api.spaces.removeIcon(space.key), 'Could not remove the picture.')
                  : setIcon(SpaceIconKind.None, null))}>
                Use the default
              </button>
            )}
          </div>
          <input ref={fileInput} type="file" accept="image/png,image/jpeg,image/webp" hidden onChange={onPickFile} />
        </div>
      </div>

      {error && <p className="alert alert--error">{error}</p>}

      <fieldset className="icon-picker__emoji" disabled={busy}>
        <legend className="muted small">Or pick an emoji</legend>
        {SUGGESTED.map((emoji) => (
          <button
            key={emoji}
            type="button"
            className={space.iconValue === emoji ? 'icon-picker__emoji-btn is-active' : 'icon-picker__emoji-btn'}
            aria-label={`Use ${emoji} as the icon`}
            aria-pressed={space.iconValue === emoji}
            onClick={() => setIcon(SpaceIconKind.Emoji, emoji)}
          >
            {emoji}
          </button>
        ))}
      </fieldset>

      <form className="icon-picker__custom" onSubmit={onCustom}>
        <label className="small">
          Any other emoji
          <input
            value={custom}
            onChange={(e) => setCustom(e.target.value)}
            placeholder="Paste one here"
            aria-label="Paste any emoji"
            maxLength={16}
          />
        </label>
        <button type="submit" className="btn btn--ghost btn--sm" disabled={busy || !custom.trim()}>Use it</button>
      </form>

      <fieldset className="icon-picker__colors" disabled={busy || isImage}>
        <legend className="muted small">
          {isImage ? 'Remove the picture to choose a tile colour' : 'Tile colour'}
        </legend>
        {SPACE_ICON_COLORS.map((color, index) => {
          const selected = !isImage && index === activeColor
          return (
            <button
              key={color}
              type="button"
              className={selected ? 'avatar-picker__swatch is-active' : 'avatar-picker__swatch'}
              style={{ background: color, borderRadius: 6 }}
              aria-label={`Tile colour ${index + 1}`}
              aria-pressed={selected}
              onClick={() => setIcon(space.iconKind, space.iconValue, index)}
            />
          )
        })}
      </fieldset>
    </div>
  )
}
