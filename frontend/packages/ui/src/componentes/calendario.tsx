import { useState } from 'react';
import { cn } from '../utils';
import { Icono } from './basicos';

const MESES = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];
const DIAS = ['L', 'M', 'M', 'J', 'V', 'S', 'D'];

const iso = (a: number, m: number, d: number) => `${a}-${String(m + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`;

/** Fecha de hoy (`yyyy-MM-dd`) en hora de México. */
function hoyMexico(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Mexico_City' }).format(new Date());
}

/**
 * Calendario mensual en español (semana de lunes a domingo) para elegir una fecha. Trabaja con fechas ISO
 * (`yyyy-MM-dd`) sin zonas horarias: son fechas de negocio.
 */
export function Calendario({
  valor,
  onElegir,
  min,
  max,
}: {
  valor: string;
  onElegir: (iso: string) => void;
  min?: string;
  max?: string;
}) {
  const hoy = hoyMexico();
  const base = valor || hoy;
  const [anio, setAnio] = useState(Number(base.slice(0, 4)));
  const [mes, setMes] = useState(Number(base.slice(5, 7)) - 1);

  const primero = new Date(Date.UTC(anio, mes, 1));
  const desplazamiento = (primero.getUTCDay() + 6) % 7; // lunes = 0
  const diasMes = new Date(Date.UTC(anio, mes + 1, 0)).getUTCDate();
  const celdas: (number | null)[] = [
    ...Array.from({ length: desplazamiento }, () => null),
    ...Array.from({ length: diasMes }, (_, i) => i + 1),
  ];
  while (celdas.length % 7 !== 0) celdas.push(null);

  const moverMes = (delta: number) => {
    const n = mes + delta;
    setAnio(anio + Math.floor(n / 12));
    setMes(((n % 12) + 12) % 12);
  };
  const fuera = (f: string) => (min && f < min) || (max && f > max);

  return (
    <div className="w-[calc(19rem*var(--tg-escala-texto))] max-w-[90vw] p-3" role="dialog" aria-label="Elegir fecha">
      <div className="mb-2 flex items-center justify-between">
        <button type="button" onClick={() => moverMes(-1)} aria-label="Mes anterior"
          className="grid size-9 cursor-pointer place-items-center rounded-full hover:bg-row-hover">
          <Icono nombre="chevron_left" tamano={22} />
        </button>
        <span className="text-sm font-medium capitalize" aria-live="polite">
          {MESES[mes]} {anio}
        </span>
        <button type="button" onClick={() => moverMes(1)} aria-label="Mes siguiente"
          className="grid size-9 cursor-pointer place-items-center rounded-full hover:bg-row-hover">
          <Icono nombre="chevron_right" tamano={22} />
        </button>
      </div>
      <div className="grid grid-cols-7 gap-0.5 text-center" role="grid">
        {DIAS.map((d, i) => (
          <span key={i} className="py-1 text-xs font-medium text-muted" aria-hidden>
            {d}
          </span>
        ))}
        {celdas.map((d, i) => {
          if (d === null) return <span key={i} />;
          const f = iso(anio, mes, d);
          const elegido = f === valor;
          const esHoy = f === hoy;
          const deshabilitado = !!fuera(f);
          return (
            <button
              key={i}
              type="button"
              disabled={deshabilitado}
              onClick={() => onElegir(f)}
              aria-pressed={elegido}
              aria-label={`${d} de ${MESES[mes]} de ${anio}`}
              className={cn(
                'aspect-square cursor-pointer rounded-full text-sm tabular-nums transition-colors disabled:cursor-not-allowed disabled:opacity-30',
                elegido ? 'bg-primary font-medium text-on-primary' : 'hover:bg-row-hover',
                !elegido && esHoy && 'border border-primary text-primary',
              )}
            >
              {d}
            </button>
          );
        })}
      </div>
      <div className="mt-2 flex justify-between border-t border-row-divider pt-2">
        <button type="button" onClick={() => onElegir('')} className="cursor-pointer rounded-full px-3 py-1.5 text-sm text-muted hover:bg-row-hover">
          Borrar
        </button>
        <button type="button" disabled={!!fuera(hoy)} onClick={() => onElegir(hoy)}
          className="cursor-pointer rounded-full px-3 py-1.5 text-sm font-medium text-primary hover:bg-soft disabled:opacity-40">
          Hoy
        </button>
      </div>
    </div>
  );
}
