/** Preview tokens the API sends for media-only messages (`[image]`), shown as words in the list. */
const previewKinds = new Set(['image', 'audio', 'video', 'file', 'sticker', 'attachment']);

/** The i18n key for a media preview, or null when the preview is the customer's own text. */
export function mediaPreviewKey(preview: string): string | null {
  const match = /^\[([a-z]+)\]$/.exec(preview);
  return match?.[1] && previewKinds.has(match[1]) ? `media.preview.${match[1]}` : null;
}
