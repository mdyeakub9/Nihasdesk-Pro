import { Badge, Button, Card } from '@nihasdesk/ui';
import { ArrowLeft, ArrowRight, ImagePlus, Package, Trash2 } from 'lucide-react';
import { useRef, useState, type ChangeEvent, type DragEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../lib/api';
import { imageSrc, imageTypes, maxImageBytes, maxImages, useProductImages, type ProductImage } from './api';

/** Server codes with their own message; anything else gets the general one. */
const knownErrors = [
  'catalog.image_too_large',
  'catalog.image_type_not_allowed',
  'catalog.too_many_images',
  'catalog.image_not_found',
] as const;

type Problem = { key: string; name?: string };

function problemOf(error: unknown): string {
  const code = error instanceof ApiError ? error.code : undefined;
  const known = knownErrors.find((k) => k === code);
  return known ? `products.images.errors.${known.slice('catalog.'.length)}` : 'products.images.errors.failed';
}

/**
 * The product's photos (PRD 4.6, ADR 0031 §12): up to 8, the first is the main photo. Uploading,
 * moving and deleting are saved at once. Order changes by dragging, or with the arrow buttons
 * (keyboard and touch). Owner and Admin only; members see the photos.
 */
export function ProductImages({
  productId,
  images,
  manage,
}: {
  productId: string;
  images: ProductImage[];
  manage: boolean;
}) {
  const { t } = useTranslation();
  const { upload, reorder, remove } = useProductImages(productId);
  const input = useRef<HTMLInputElement>(null);
  const [problems, setProblems] = useState<Problem[]>([]);
  const [uploading, setUploading] = useState(0);
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);
  const [dragged, setDragged] = useState<string | null>(null);
  const free = maxImages - images.length;
  const busy = uploading > 0 || reorder.isPending || remove.isPending;

  /** One at a time, as many as there is room for; the server checks each file again (SEC-04). */
  const add = async (event: ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(event.target.files ?? []);
    event.target.value = '';
    const found: Problem[] = [];
    let room = free;
    const accepted: File[] = [];
    for (const file of files) {
      if (room <= 0) {
        found.push({ key: 'products.images.errors.too_many_images', name: file.name });
      } else if (!(imageTypes as readonly string[]).includes(file.type)) {
        found.push({ key: 'products.images.errors.image_type_not_allowed', name: file.name });
      } else if (file.size > maxImageBytes) {
        found.push({ key: 'products.images.errors.image_too_large', name: file.name });
      } else {
        accepted.push(file);
        room--;
      }
    }

    setProblems(found);
    setUploading(accepted.length);
    for (const file of accepted) {
      try {
        await upload.mutateAsync(file);
      } catch (error) {
        found.push({ key: problemOf(error), name: file.name });
        setProblems([...found]);
      } finally {
        setUploading((n) => n - 1);
      }
    }
  };

  const move = (from: number, to: number) => {
    if (to < 0 || to >= images.length || from === to) return;
    const ids = images.map((i) => i.id);
    const [id] = ids.splice(from, 1);
    if (id === undefined) return;
    ids.splice(to, 0, id);
    setProblems([]);
    reorder.mutate(ids, { onError: (error) => setProblems([{ key: problemOf(error) }]) });
  };

  const drop = (event: DragEvent, target: number) => {
    event.preventDefault();
    const from = images.findIndex((i) => i.id === dragged);
    setDragged(null);
    if (from >= 0) move(from, target);
  };

  return (
    <Card className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <h2 className="flex-1 text-base font-semibold">
          {t('products.images.title', { count: images.length, max: maxImages })}
        </h2>
        {manage && (
          <>
            <input
              ref={input}
              type="file"
              multiple
              accept={imageTypes.join(',')}
              className="sr-only"
              aria-label={t('products.images.choose')}
              onChange={(event) => void add(event)}
            />
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={free <= 0 || busy}
              onClick={() => input.current?.click()}
            >
              <ImagePlus aria-hidden="true" />
              {t('products.images.add')}
            </Button>
          </>
        )}
      </div>

      <p className="text-[13px] text-secondary-foreground">{t('products.images.hint')}</p>

      {uploading > 0 && (
        <p role="status" className="text-[13px] text-secondary-foreground">
          {t('products.images.uploading', { count: uploading })}
        </p>
      )}
      {problems.length > 0 && (
        <ul
          role="alert"
          className="flex list-disc flex-col gap-1 rounded-lg border bg-warning px-4 py-3 pl-8 text-[13px] text-warning-foreground"
        >
          {problems.map((p, i) => (
            <li key={i}>{p.name ? `${p.name}: ${t(p.key)}` : t(p.key)}</li>
          ))}
        </ul>
      )}

      {images.length === 0 ? (
        <p className="text-[13px] text-secondary-foreground">{t('products.images.none')}</p>
      ) : (
        <ol className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {images.map((image, index) => {
            const label = t('products.images.photoNumber', { number: index + 1 });
            return (
              <li
                key={image.id}
                aria-label={label}
                draggable={manage && !busy}
                onDragStart={() => setDragged(image.id)}
                onDragEnd={() => setDragged(null)}
                onDragOver={(event) => manage && dragged && event.preventDefault()}
                onDrop={(event) => drop(event, index)}
                className={`flex flex-col gap-2 rounded-lg border p-2 ${dragged === image.id ? 'opacity-50' : ''}`}
              >
                <div className="relative aspect-square overflow-hidden rounded-md bg-subtle">
                  {image.thumbnailUrl ? (
                    <img
                      src={imageSrc(image.thumbnailUrl)}
                      alt={label}
                      draggable={false}
                      className="size-full object-cover"
                    />
                  ) : (
                    <span className="flex size-full items-center justify-center p-2 text-center text-[12px] text-secondary-foreground">
                      {t(`products.images.status.${image.status}`)}
                    </span>
                  )}
                  {index === 0 && (
                    <Badge tone="success" className="absolute top-1 left-1">
                      {t('products.images.main')}
                    </Badge>
                  )}
                </div>
                {manage && (
                  <div className="flex flex-wrap items-center gap-1">
                    <Button
                      type="button"
                      size="icon"
                      variant="ghost"
                      disabled={busy || index === 0}
                      aria-label={t('products.images.moveLeft', { number: index + 1 })}
                      onClick={() => move(index, index - 1)}
                    >
                      <ArrowLeft aria-hidden="true" />
                    </Button>
                    <Button
                      type="button"
                      size="icon"
                      variant="ghost"
                      disabled={busy || index === images.length - 1}
                      aria-label={t('products.images.moveRight', { number: index + 1 })}
                      onClick={() => move(index, index + 1)}
                    >
                      <ArrowRight aria-hidden="true" />
                    </Button>
                    <span className="flex-1" />
                    {confirmDelete === image.id ? (
                      <>
                        <Button
                          type="button"
                          size="sm"
                          variant="destructive"
                          disabled={busy}
                          onClick={() =>
                            remove.mutate(image.id, {
                              onSettled: () => setConfirmDelete(null),
                              onError: (error) => setProblems([{ key: problemOf(error) }]),
                            })
                          }
                        >
                          {t('products.images.confirmDelete')}
                        </Button>
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          onClick={() => setConfirmDelete(null)}
                        >
                          {t('team.cancel')}
                        </Button>
                      </>
                    ) : (
                      <Button
                        type="button"
                        size="icon"
                        variant="ghost"
                        disabled={busy}
                        aria-label={t('products.images.delete', { number: index + 1 })}
                        onClick={() => setConfirmDelete(image.id)}
                      >
                        <Trash2 aria-hidden="true" />
                      </Button>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ol>
      )}
    </Card>
  );
}

/** The main photo's thumbnail in lists, or a placeholder of the same size. */
export function ProductThumb({ url, className = 'size-10' }: { url: string | null; className?: string }) {
  return url ? (
    <img
      src={imageSrc(url)}
      alt=""
      loading="lazy"
      className={`${className} shrink-0 rounded-md border object-cover`}
    />
  ) : (
    <span
      aria-hidden="true"
      className={`${className} flex shrink-0 items-center justify-center rounded-md border bg-subtle`}
    >
      <Package className="size-4 text-muted-foreground" strokeWidth={1.5} />
    </span>
  );
}
