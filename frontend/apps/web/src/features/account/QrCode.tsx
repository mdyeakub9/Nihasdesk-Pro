import qrcode from 'qrcode-generator';
import { useMemo } from 'react';

/**
 * QR code drawn in the browser: the MFA secret never leaves the page for a QR service. Rendered as
 * an <img> from an SVG data URL, so no HTML is injected (SEC-02).
 */
export function QrCode({ value, label }: { value: string; label: string }) {
  const src = useMemo(() => {
    const qr = qrcode(0, 'M');
    qr.addData(value);
    qr.make();
    return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(qr.createSvgTag({ cellSize: 4, margin: 2, scalable: true }))}`;
  }, [value]);

  return <img src={src} alt={label} width={176} height={176} className="rounded-md border bg-white p-2" />;
}
