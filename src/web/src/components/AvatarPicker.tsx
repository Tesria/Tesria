import { type ChangeEvent, useRef, useState } from 'react'
import { api, ApiError } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { Avatar } from './Avatar'
import { AVATAR_COLORS, avatarVariantFor } from './avatarIdentity'

/** Longest edge sent to the server. It re-encodes to 256 anyway; cropping and
 *  downscaling here keeps a 12MP phone photo from being uploaded to produce a
 *  thumbnail, which is slow on a bad connection and rejected over 1 MB. */
const UPLOAD_SIZE = 512

/**
 * Centre-crops an image file to a square and scales it to {@link UPLOAD_SIZE},
 * as a PNG blob.
 *
 * `createImageBitmap` rather than an `<img>` with an object URL: it decodes off
 * the main thread, needs no load-event dance, and — the part that matters —
 * honours EXIF orientation, so a portrait phone photo does not arrive sideways.
 */
async function cropToSquare(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
  try {
    const edge = Math.min(bitmap.width, bitmap.height)
    const sx = (bitmap.width - edge) / 2
    const sy = (bitmap.height - edge) / 2

    const canvas = document.createElement('canvas')
    canvas.width = UPLOAD_SIZE
    canvas.height = UPLOAD_SIZE
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new Error('Canvas is unavailable in this browser.')
    ctx.imageSmoothingQuality = 'high'
    ctx.drawImage(bitmap, sx, sy, edge, edge, 0, 0, UPLOAD_SIZE, UPLOAD_SIZE)

    return await new Promise<Blob>((resolve, reject) => {
      canvas.toBlob(
        (blob) => (blob ? resolve(blob) : reject(new Error('Could not read that image.'))),
        'image/png',
      )
    })
  } finally {
    bitmap.close()
  }
}

/**
 * The avatar section of the profile page: the current avatar, the twelve
 * generated ones to choose between, and upload/remove.
 *
 * The crop is deliberate rather than cosmetic — the server centre-crops too,
 * so without it the user would upload a photo and be shown a different part of
 * it than they expected. Doing it here means what they see is what is stored.
 */
export function AvatarPicker() {
  const { user, refresh } = useAuth()
  const fileInput = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!user) return null

  const hasUpload = Boolean(user.avatarHash)
  const activeVariant = avatarVariantFor(user)

  async function withBusy(work: () => Promise<void>, fallback: string) {
    setBusy(true)
    setError(null)
    try {
      await work()
      await refresh()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : fallback)
    } finally {
      setBusy(false)
    }
  }

  async function onPickFile(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = '' // so re-picking the same file fires change again
    if (!file) return

    await withBusy(async () => {
      const square = await cropToSquare(file)
      await api.avatar.upload(square)
    }, 'Could not upload that image.')
  }

  return (
    <div className="avatar-picker">
      <div className="avatar-picker__current">
        <Avatar subject={user} size={64} />
        <div>
          <p className="muted small">
            {hasUpload
              ? 'Your uploaded picture.'
              : 'Generated from your name. Pick a colour, or upload a picture.'}
          </p>
          <div className="row-gap">
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              onClick={() => fileInput.current?.click()}
              disabled={busy}
            >
              {hasUpload ? 'Replace picture' : 'Upload picture'}
            </button>
            {hasUpload && (
              <button
                type="button"
                className="btn btn--ghost btn--sm"
                onClick={() => withBusy(() => api.avatar.remove(), 'Could not remove your picture.')}
                disabled={busy}
              >
                Remove
              </button>
            )}
          </div>
          <input
            ref={fileInput}
            type="file"
            accept="image/png,image/jpeg,image/webp"
            hidden
            onChange={onPickFile}
          />
        </div>
      </div>

      {error && <p className="alert alert--error">{error}</p>}

      <fieldset className="avatar-picker__variants" disabled={busy}>
        <legend className="muted small">
          {hasUpload ? 'Remove your picture to use a generated avatar' : 'Generated avatar'}
        </legend>
        {AVATAR_COLORS.map((color, index) => {
          const selected = !hasUpload && index === activeVariant
          return (
            <button
              key={color}
              type="button"
              className={selected ? 'avatar-picker__swatch is-active' : 'avatar-picker__swatch'}
              style={{ background: color }}
              aria-label={`Avatar colour ${index + 1}`}
              aria-pressed={selected}
              onClick={() => withBusy(() => api.avatar.setVariant(index), 'Could not change your avatar.')}
            />
          )
        })}
      </fieldset>
    </div>
  )
}
