import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

/**
 * The file name a downloaded document is saved under, read from the server's `Content-Disposition`
 * (Phase 14 Slice 3a, D113).
 *
 * The server names the file (`Rechnung-RE-2026-00017.pdf`) with the same rule the email attachment
 * uses, so the Dashboard reads that name rather than inventing its own. ASP.NET sends both forms —
 * `filename=` and the RFC 5987 `filename*=UTF-8''…` — and the encoded one is preferred when present.
 * `fallback` is used only when the header is missing or unreadable.
 */
export function fileNameFromContentDisposition(header: string | null, fallback: string): string {
  if (!header) {
    return fallback;
  }

  const encoded = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(header);
  if (encoded) {
    try {
      return sanitize(decodeURIComponent(encoded[1].trim()), fallback);
    } catch {
      // A malformed escape sequence: fall through to the plain form.
    }
  }

  const plain = /filename\s*=\s*"?([^";]+)"?/i.exec(header);
  return plain ? sanitize(plain[1].trim(), fallback) : fallback;
}

/** A path separator or a control character never reaches the saved name. */
function sanitize(name: string, fallback: string): string {
  // eslint-disable-next-line no-control-regex
  const cleaned = name.replace(/[\\/\u0000-\u001f]/g, '-').trim();
  return cleaned.length > 0 ? cleaned : fallback;
}

/**
 * Hands a fetched file to the browser as a download.
 *
 * **Why a fetch and not a link:** the Dashboard authenticates with a bearer token held in memory
 * (D73), which an ordinary `<a href>` cannot carry. So the file is fetched through `HttpClient`, and
 * this turns the response into a download: an object URL, a temporary anchor with `download`, a
 * click, and the URL revoked straight after so the bytes do not linger in memory.
 *
 * A service rather than a function so a component test can substitute it — a real anchor click in a
 * test runner would start a real download.
 */
@Injectable({ providedIn: 'root' })
export class DocumentDownload {
  private readonly document = inject(DOCUMENT);

  save(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);

    try {
      const anchor = this.document.createElement('a');
      anchor.href = url;
      anchor.download = fileName;
      anchor.rel = 'noopener';
      anchor.style.display = 'none';

      this.document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
    } finally {
      URL.revokeObjectURL(url);
    }
  }
}
