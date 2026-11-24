/** CSV for Excel: `;` as separator, CRLF line ends; with bom, a UTF-8 byte order mark so Excel reads umlauts. */
export function toCsv(rows: (string | number | boolean | null | undefined)[][], bom = false): string {
  const text = rows.map(r => r.map(v => cell(v == null ? '' : String(v))).join(';')).join('\r\n');
  return bom ? '﻿' + text : text;
}

/** A CSV cell: quoted when needed; text that Excel would run as a formula is prefixed with an apostrophe. */
function cell(v: string): string {
  const safe = /^[=+\-@\t\r]/.test(v) ? `'${v}` : v;
  return /[;"\r\n]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
}

/** Offers a blob as a download. */
export function saveBlob(blob: Blob, fileName: string): void {
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(link.href);
}

/** Excel saves "CSV UTF-8" or, as plain "CSV", Windows-1252; try UTF-8 first. */
export function decodeCsv(buffer: ArrayBuffer): string {
  try { return new TextDecoder('utf-8', { fatal: true }).decode(buffer); } catch { return new TextDecoder('windows-1252').decode(buffer); }
}
