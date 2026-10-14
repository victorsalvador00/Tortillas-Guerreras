import { esErrorApi, mensajeError, useListarRutas } from '@tg/api-client';
import { Selector } from '@tg/ui';
import type { Path } from 'react-hook-form';

/** Lista de rutas activas (`R01 · Centro`). */
export function SelectorRuta({
  etiqueta = 'Ruta',
  vacio,
  value,
  onChange,
  error,
  className,
  disabled,
}: {
  etiqueta?: string;
  vacio?: string;
  value: string;
  onChange: (valor: string) => void;
  error?: string;
  className?: string;
  disabled?: boolean;
}) {
  const { data } = useListarRutas({ activo: true });
  return (
    <Selector
      etiqueta={etiqueta}
      vacio={vacio}
      opciones={(data ?? []).map((r) => ({ valor: r.id, etiqueta: `${r.codigo} · ${r.nombre}` }))}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      error={error}
      className={className}
      disabled={disabled}
    />
  );
}

/** Pasa los errores por campo del API al formulario y devuelve el mensaje general. */
export function aplicarErroresApi<T extends Record<string, unknown>>(
  e: unknown,
  setError: (campo: Path<T>, error: { message: string }) => void,
): string {
  if (esErrorApi(e)) {
    Object.entries(e.errores).forEach(([campo, mensajes]) => setError(campo as Path<T>, { message: mensajes[0] }));
  }
  return mensajeError(e);
}

export const DIAS_SEMANA = [
  { valor: 1, corto: 'L', nombre: 'Lunes' },
  { valor: 2, corto: 'M', nombre: 'Martes' },
  { valor: 3, corto: 'X', nombre: 'Miércoles' },
  { valor: 4, corto: 'J', nombre: 'Jueves' },
  { valor: 5, corto: 'V', nombre: 'Viernes' },
  { valor: 6, corto: 'S', nombre: 'Sábado' },
  { valor: 7, corto: 'D', nombre: 'Domingo' },
];

/** Día de la semana (1 = lunes … 7 = domingo) de una fecha `yyyy-MM-dd`. */
export function diaSemana(fecha: string): number {
  const [a, m, d] = fecha.split('-').map(Number);
  const dia = new Date(Date.UTC(a, m - 1, d)).getUTCDay();
  return dia === 0 ? 7 : dia;
}
