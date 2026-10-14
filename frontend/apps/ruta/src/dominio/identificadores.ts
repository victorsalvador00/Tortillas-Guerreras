/**
 * UUID v7 (regla 6): los genera el teléfono para ventas, retornos, cobros y `opId`. 48 bits de milisegundos + 74 bits
 * aleatorios; ordenables por tiempo.
 */
export function uuidv7(bytesAleatorios: (n: number) => Uint8Array, ahoraMs: number = Date.now()): string {
  const b = new Uint8Array(16);
  b.set(bytesAleatorios(10), 6);
  let ms = Math.floor(ahoraMs);
  for (let i = 5; i >= 0; i--) {
    b[i] = ms % 256;
    ms = Math.floor(ms / 256);
  }
  b[6] = (b[6] & 0x0f) | 0x70; // versión 7
  b[8] = (b[8] & 0x3f) | 0x80; // variante RFC 4122
  const h = Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

/** Folio de la app: `<ruta>-<dispositivo>-<consecutivo 6 dígitos>` (RN-VEN-09). */
export function formatearFolio(rutaCodigo: string, dispositivoCodigo: string, consecutivo: number): string {
  if (!Number.isInteger(consecutivo) || consecutivo <= 0) {
    throw new Error(`Consecutivo de folio inválido: ${consecutivo}`);
  }
  return `${rutaCodigo}-${dispositivoCodigo}-${String(consecutivo).padStart(6, '0')}`;
}

/**
 * RN-SYN-08: el consecutivo nunca se reinicia. Al reinstalar se toma el mayor entre el local y el que conoce el
 * servidor; el siguiente folio usa ese valor + 1.
 */
export function consecutivoInicial(local: number | null | undefined, servidor: number | null | undefined): number {
  return Math.max(local ?? 0, servidor ?? 0);
}
