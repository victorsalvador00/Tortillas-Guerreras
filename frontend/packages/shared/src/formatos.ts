import { formatInTimeZone, toZonedTime } from 'date-fns-tz';
import { es } from 'date-fns/locale';

export const ZONA_HORARIA = 'America/Mexico_City';

const moneda = new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' });

/** $1,234.50 — recibe pesos. */
export function formatoMoneda(pesos: number | null | undefined): string {
  return pesos == null ? '—' : moneda.format(pesos);
}

/** $1,234.50 — recibe centavos. */
export function formatoCentavos(centavos: number | null | undefined): string {
  return centavos == null ? '—' : moneda.format(centavos / 100);
}

/** 1,234.5 — cantidades con hasta `decimales` decimales (sin ceros sobrantes). */
export function formatoCantidad(valor: number | null | undefined, decimales = 4): string {
  if (valor == null) return '—';
  return new Intl.NumberFormat('es-MX', { maximumFractionDigits: decimales }).format(valor);
}

/** 16 % — recibe tasa (0.16). */
export function formatoPorcentaje(tasa: number | null | undefined, decimales = 2): string {
  if (tasa == null) return '—';
  return new Intl.NumberFormat('es-MX', { style: 'percent', maximumFractionDigits: decimales }).format(tasa);
}

function aFecha(valor: string | Date): Date {
  return typeof valor === 'string' ? new Date(valor) : valor;
}

/** 03/11/2026 14:35 en hora de México. */
export function formatoFechaHora(valor: string | Date | null | undefined): string {
  if (!valor) return '—';
  return formatInTimeZone(aFecha(valor), ZONA_HORARIA, 'dd/MM/yyyy HH:mm', { locale: es });
}

/**
 * Fecha de negocio (`DateOnly` del API, "2026-11-03") → 03/11/2026. No se convierte de zona: ya es fecha local.
 */
export function formatoFecha(valor: string | null | undefined): string {
  if (!valor) return '—';
  const [anio, mes, dia] = valor.slice(0, 10).split('-');
  return `${dia}/${mes}/${anio}`;
}

/** Igual que `formatoFecha` (DD/MM/AAAA en todo el sistema); se conserva el nombre para quien ya lo usa. */
export function formatoFechaLarga(valor: string | null | undefined): string {
  return formatoFecha(valor);
}

/** Fecha operativa (`yyyy-MM-dd`) de un instante en America/Mexico_City (regla 7). */
export function fechaOperativa(instante: Date = new Date()): string {
  return formatInTimeZone(instante, ZONA_HORARIA, 'yyyy-MM-dd');
}

/** Suma días a una fecha `yyyy-MM-dd`. */
export function sumarDias(fecha: string, dias: number): string {
  const [anio, mes, dia] = fecha.split('-').map(Number);
  const d = new Date(Date.UTC(anio, mes - 1, dia + dias));
  return d.toISOString().slice(0, 10);
}

/** Hora local de México de un instante. */
export function horaLocal(instante: string | Date): Date {
  return toZonedTime(aFecha(instante), ZONA_HORARIA);
}
