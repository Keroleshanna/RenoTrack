import { TestBed } from '@angular/core/testing';

import { DocumentDownload, fileNameFromContentDisposition } from './document-download';

/**
 * Turning a fetched file into a download (Phase 14 Slice 3a, D113). The file name is the server's,
 * so reading it correctly is what keeps the Admin's copy named exactly as the customer's attachment.
 */
describe('Document download', () => {
  describe('file name from Content-Disposition', () => {
    it('reads the encoded form ASP.NET sends alongside the plain one', () => {
      expect(
        fileNameFromContentDisposition(
          "attachment; filename=Rechnung-RE-2026-00017.pdf; filename*=UTF-8''Rechnung-RE-2026-00017.pdf",
          'fallback.pdf',
        ),
      ).toBe('Rechnung-RE-2026-00017.pdf');
    });

    it('decodes a percent-encoded name', () => {
      expect(
        fileNameFromContentDisposition("attachment; filename*=UTF-8''Rechnung%20%C3%84.pdf", 'fallback.pdf'),
      ).toBe('Rechnung Ä.pdf');
    });

    it('reads a quoted plain name', () => {
      expect(fileNameFromContentDisposition('attachment; filename="Rechnung-RE-1.pdf"', 'fallback.pdf')).toBe(
        'Rechnung-RE-1.pdf',
      );
    });

    it('falls back when the header is absent or carries no name', () => {
      expect(fileNameFromContentDisposition(null, 'Rechnung-RE-1.pdf')).toBe('Rechnung-RE-1.pdf');
      expect(fileNameFromContentDisposition('attachment', 'Rechnung-RE-1.pdf')).toBe('Rechnung-RE-1.pdf');
    });

    it('never lets a path separator into the saved name', () => {
      expect(fileNameFromContentDisposition('attachment; filename="../etc/passwd"', 'fallback.pdf')).toBe(
        '..-etc-passwd',
      );
      expect(
        fileNameFromContentDisposition("attachment; filename*=UTF-8''..%2F..%5Cevil.pdf", 'fallback.pdf'),
      ).toBe('..-..-evil.pdf');
    });

    it('falls back on a malformed encoding rather than throwing', () => {
      expect(
        fileNameFromContentDisposition("attachment; filename*=UTF-8''%E0%A4%A; filename=plain.pdf", 'fallback.pdf'),
      ).toBe('plain.pdf');
    });
  });

  describe('saving', () => {
    it('downloads through a temporary anchor and releases the object URL straight away', () => {
      const created = spyOn(URL, 'createObjectURL').and.returnValue('blob:document');
      const revoked = spyOn(URL, 'revokeObjectURL');
      const clicked = spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) {
        expect(this.download).toBe('Rechnung-RE-1.pdf');
        expect(this.href).toBe('blob:document');
        expect(document.body.contains(this)).toBeTrue();
      });
      const blob = new Blob(['%PDF-'], { type: 'application/pdf' });

      TestBed.inject(DocumentDownload).save(blob, 'Rechnung-RE-1.pdf');

      expect(created).toHaveBeenCalledOnceWith(blob);
      expect(clicked).toHaveBeenCalledTimes(1);
      expect(revoked).toHaveBeenCalledOnceWith('blob:document');
      expect(document.querySelector('a[download]')).toBeNull();
    });
  });
});
