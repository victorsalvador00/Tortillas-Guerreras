import type { ConteoPartidaDto, ExistenciaDto } from '@tg/api-client';
import { fechaOperativa, sumarDias } from '@tg/shared';

/** Días antes de caducar en que un lote se marca como próximo a caducar. */
export const DIAS_AVISO_CADUCIDAD = 2;

export type AlertaExistencia = 'caducado' | 'por-caducar' | 'bajo-minimo' | null;

/** Alerta visual de una fila de existencias (caducidad primero, luego mínimo). */
export function alertaExistencia(e: Pick<ExistenciaDto, 'cantidad' | 'fechaCaducidad' | 'stockMinimo'>, hoy = fechaOperativa()): AlertaExistencia {
  if (e.fechaCaducidad && e.cantidad > 0) {
    if (e.fechaCaducidad < hoy) return 'caducado';
    if (e.fechaCaducidad <= sumarDias(hoy, DIAS_AVISO_CADUCIDAD)) return 'por-caducar';
  }
  if (e.stockMinimo != null && e.cantidad < e.stockMinimo) return 'bajo-minimo';
  return null;
}

/** Resumen de los ajustes que generará cerrar el conteo (RN-INV-09). */
export function resumenCierre(partidas: Pick<ConteoPartidaDto, 'diferencia'>[]) {
  const conDiferencia = partidas.filter((p) => p.diferencia !== 0);
  return {
    partidas: conDiferencia.length,
    entradas: conDiferencia.filter((p) => p.diferencia > 0).length,
    salidas: conDiferencia.filter((p) => p.diferencia < 0).length,
  };
}
