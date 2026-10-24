import type { DestinoSobrante, LiquidacionPartidaDto } from '@tg/api-client';

/** Captura de una partida en pantalla (texto del campo para no perder lo que se escribe). */
export interface CapturaPartida {
  contado: string;
  destino: DestinoSobrante;
  motivoMermaId: string;
  /** RN-LOG-24: cantidad a cobro al chofer (vacío = la sugerida). */
  aCobro?: string;
}

export function llavePartida(p: Pick<LiquidacionPartidaDto, 'productoId' | 'lote'>): string {
  return `${p.productoId}|${p.lote}`;
}

/** Redondeo a 4 decimales (numeric(18,4)) sin arrastrar errores de punto flotante. */
export function redondear4(valor: number): number {
  return Math.round(valor * 10_000) / 10_000;
}

/** RN-LOG-06: diferencia = sobrante contado − sobrante esperado; `null` si aún no se cuenta. */
export function diferencia(esperado: number, contado: string): number | null {
  if (contado.trim() === '') return null;
  const n = Number(contado);
  return Number.isFinite(n) ? redondear4(n - esperado) : null;
}

export interface ResumenCierre {
  sinContar: number;
  reintegrar: number;
  merma: number;
  reproceso: number;
  faltantes: number;
  sobrantes: number;
  conDiferencia: number;
}

/**
 * Lo que generará el cierre (RN-LOG-07): reintegro a PT, mermas/reproceso y ajustes por diferencia con motivo
 * "Faltante de ruta". Las partidas con esperado 0 y sin captura se consideran contadas en 0.
 */
export function resumenCierre(partidas: LiquidacionPartidaDto[], capturas: Record<string, CapturaPartida>): ResumenCierre {
  const r: ResumenCierre = { sinContar: 0, reintegrar: 0, merma: 0, reproceso: 0, faltantes: 0, sobrantes: 0, conDiferencia: 0 };
  for (const p of partidas) {
    const c = capturas[llavePartida(p)];
    const texto = c?.contado ?? '';
    if (texto.trim() === '' && p.sobranteEsperado !== 0) {
      r.sinContar++;
      continue;
    }
    const contado = texto.trim() === '' ? 0 : Number(texto);
    const destino = c?.destino ?? 'Reintegrar';
    if (contado > 0) {
      if (destino === 'Reintegrar') r.reintegrar = redondear4(r.reintegrar + contado);
      else if (destino === 'Merma') r.merma = redondear4(r.merma + contado);
      else r.reproceso = redondear4(r.reproceso + contado);
    }
    const d = redondear4(contado - p.sobranteEsperado);
    if (d !== 0) {
      r.conDiferencia++;
      if (d < 0) r.faltantes = redondear4(r.faltantes - d);
      else r.sobrantes = redondear4(r.sobrantes + d);
    }
  }
  return r;
}

export function capturasIniciales(partidas: LiquidacionPartidaDto[]): Record<string, CapturaPartida> {
  return Object.fromEntries(
    partidas.map((p) => [
      llavePartida(p),
      {
        contado: p.sobranteContado == null ? '' : String(p.sobranteContado),
        destino: p.destinoSobrante,
        motivoMermaId: p.motivoMermaId ?? '',
        aCobro: p.aplicaACobro ? String(p.cantidadACobro ?? p.cantidadACobroSugerida) : '',
      },
    ]),
  );
}
