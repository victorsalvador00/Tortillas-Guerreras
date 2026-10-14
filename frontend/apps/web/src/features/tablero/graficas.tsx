import { formatoCantidad, formatoMoneda } from '@tg/shared';
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

/**
 * Gráficas de barras del tablero: marcas delgadas con extremos redondeados, cuadrícula recesiva, una sola escala y
 * tooltip al pasar el cursor. Colores: espacios categóricos validados en orden fijo (--tg-serie-*); el texto usa
 * tintas de texto, nunca el color de la serie.
 */

const ejes = { stroke: 'var(--tg-outline-variant)', tick: { fill: 'var(--tg-on-surface-variant)', fontSize: 16 } };

function TooltipTarjeta({
  active,
  payload,
  label,
  formato,
}: {
  active?: boolean;
  payload?: { name: string; value: number; color: string }[];
  label?: string;
  formato: (v: number) => string;
}) {
  if (!active || !payload?.length) return null;
  return (
    <div className="rounded-xl border border-outline-variant bg-card px-3 py-2 text-13 shadow-[0_8px_24px_rgb(0_0_0/18%)]">
      <p className="m-0 mb-1 font-medium">{label}</p>
      {payload.map((p) => (
        <p key={p.name} className="m-0 flex items-center gap-2">
          <span className="inline-block size-2.5 rounded-sm" style={{ background: p.color }} aria-hidden />
          <span className="text-muted">{p.name}</span>
          <span className="ml-auto font-medium tabular-nums">{formato(p.value)}</span>
        </p>
      ))}
    </div>
  );
}

export interface SerieBarra {
  clave: string;
  nombre: string;
}

/** Barras agrupadas por categoría; con una sola serie no hay leyenda (el título la nombra). */
export function GraficaBarras<T extends Record<string, unknown>>({
  datos,
  categoria,
  series,
  formato = (v) => formatoCantidad(v),
  alto = 260,
  descripcion,
}: {
  datos: T[];
  categoria: keyof T & string;
  series: SerieBarra[];
  formato?: (v: number) => string;
  alto?: number;
  descripcion: string;
}) {
  return (
    <figure className="m-0" aria-label={descripcion}>
      <ResponsiveContainer width="100%" height={alto}>
        <BarChart data={datos} margin={{ top: 8, right: 8, bottom: 0, left: 0 }} barGap={2} barCategoryGap="30%">
          <CartesianGrid vertical={false} stroke="var(--tg-row-divider)" />
          <XAxis dataKey={categoria} {...ejes} tickLine={false} />
          <YAxis {...ejes} tickLine={false} axisLine={false} width={88} tickFormatter={(v: number) => formato(v)} />
          <Tooltip
            cursor={{ fill: 'var(--tg-row-hover)' }}
            content={(p) => <TooltipTarjeta {...(p as object)} formato={formato} />}
          />
          {series.length > 1 && <Legend wrapperStyle={{ fontSize: 17, color: 'var(--tg-on-surface-variant)' }} iconType="square" />}
          {series.map((s, i) => (
            <Bar key={s.clave} dataKey={s.clave} name={s.nombre} fill={`var(--tg-serie-${i + 1})`} radius={[4, 4, 0, 0]} maxBarSize={36} />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </figure>
  );
}

export const formatoPesos = (v: number) => formatoMoneda(v).replace('.00', '');
