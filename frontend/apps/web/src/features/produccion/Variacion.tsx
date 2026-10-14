import { useReporteVariacionProduccion, type ProductoDto, type VariacionOrdenDto } from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoMoneda, formatoPorcentaje, sumarDias } from '@tg/shared';
import { Aviso, Boton, Campo, EtiquetaEstado, TablaDatos, Tarjeta, cn, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { Bar, BarChart, CartesianGrid, Cell, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { BarraFiltros, SelectorProducto, exportarCsv } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';

export interface VariacionAgregada {
  componente: string;
  teorico: number;
  real: number;
  variacion: number;
}

/** Variación por componente en el periodo: (Σ real − Σ teórico ajustado) / Σ teórico ajustado. */
export function agregarPorComponente(ordenes: VariacionOrdenDto[]): VariacionAgregada[] {
  const mapa = new Map<string, { componente: string; teorico: number; real: number }>();
  for (const o of ordenes) {
    for (const c of o.componentes) {
      const actual = mapa.get(c.productoId) ?? { componente: c.producto, teorico: 0, real: 0 };
      actual.teorico += c.teoricoAjustado;
      actual.real += c.real;
      mapa.set(c.productoId, actual);
    }
  }
  return [...mapa.values()]
    .filter((x) => x.teorico > 0)
    .map((x) => ({ ...x, variacion: (x.real - x.teorico) / x.teorico }))
    .sort((a, b) => Math.abs(b.variacion) - Math.abs(a.variacion));
}

/** Reporte teórico vs real (RN-PRO-08): se marca lo que supera el umbral configurado. */
export function Variacion() {
  const [desde, setDesde] = useState(sumarDias(fechaOperativa(), -30));
  const [hasta, setHasta] = useState(fechaOperativa());
  const [producto, setProducto] = useState<ProductoDto | null>(null);
  const { data, isLoading, isFetching } = useReporteVariacionProduccion({ desde, hasta, productoId: producto?.id });
  const agregado = useMemo(() => agregarPorComponente(data?.ordenes ?? []), [data]);
  const umbral = data?.umbral ?? 0.05;

  const columnas = useMemo<ColumnDef<VariacionOrdenDto, unknown>[]>(
    () => [
      { header: 'Orden', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Turno', accessorKey: 'turno', meta: { ocultarEnMovil: true } },
      { header: 'Producto', accessorKey: 'producto' },
      {
        header: 'Planeado / producido',
        id: 'cantidades',
        cell: ({ row }) => `${formatoCantidad(row.original.cantidadPlaneada)} / ${formatoCantidad(row.original.cantidadProducida)}`,
        meta: { alinear: 'fin' },
      },
      { header: 'Merma', accessorKey: 'mermaTotal', cell: ({ row }) => formatoCantidad(row.original.mermaTotal), meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Costo unitario', accessorKey: 'costoUnitario', cell: ({ row }) => formatoMoneda(row.original.costoUnitario), meta: { alinear: 'fin', ocultarEnMovil: true } },
      {
        header: 'Resultado',
        accessorKey: 'fueraDeUmbral',
        cell: ({ row }) =>
          row.original.fueraDeUmbral ? <EtiquetaEstado etiqueta="Fuera de umbral" color="rojo" /> : <EtiquetaEstado etiqueta="En rango" color="verde" />,
      },
    ],
    [],
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Variación de producción"
        subtitulo={`Consumo real contra teórico por componente. Umbral ±${formatoPorcentaje(umbral)} (parámetro produccion.umbral_variacion_pct).`}
        acciones={
          <Boton
            variante="secundario"
            icono="download"
            disabled={!data?.ordenes.length}
            onClick={() =>
              exportarCsv(
                'variacion-produccion',
                (data?.ordenes ?? []).flatMap((o) => o.componentes.map((c) => ({ o, c }))),
                [
                  { titulo: 'Orden', valor: (x) => x.o.folio },
                  { titulo: 'Fecha', valor: (x) => x.o.fechaOperativa },
                  { titulo: 'Producto', valor: (x) => x.o.producto },
                  { titulo: 'Componente', valor: (x) => x.c.producto },
                  { titulo: 'Teórico', valor: (x) => x.c.teoricoAjustado },
                  { titulo: 'Real', valor: (x) => x.c.real },
                  { titulo: 'Variación', valor: (x) => x.c.variacionPct ?? '' },
                ],
              )
            }
          >
            Exportar CSV
          </Boton>
        }
      />
      <BarraFiltros>
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
        <SelectorProducto etiqueta="Producto" valor={producto?.id} producto={producto} filtro={{ esProducible: true }} onCambiar={setProducto} />
      </BarraFiltros>

      {agregado.length > 0 && (
        <Tarjeta titulo="Variación por componente" subtitulo="Positivo: se consumió de más; negativo: de menos." className="mb-4">
          <div className="h-72 w-full">
            <ResponsiveContainer>
              <BarChart data={agregado.map((a) => ({ ...a, pct: Math.round(a.variacion * 1000) / 10 }))} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--tg-row-divider)" vertical={false} />
                <XAxis dataKey="componente" tick={{ fontSize: 16, fill: 'var(--tg-on-surface-variant)' }} interval={0} />
                <YAxis unit=" %" tick={{ fontSize: 16, fill: 'var(--tg-on-surface-variant)' }} />
                <Tooltip
                  formatter={(v) => [`${v} %`, 'Variación']}
                  contentStyle={{ background: 'var(--tg-card-bg)', borderRadius: 12, border: '1px solid var(--tg-outline-variant)' }}
                />
                <ReferenceLine y={umbral * 100} stroke="var(--tg-status-orange-fg)" strokeDasharray="4 4" />
                <ReferenceLine y={-umbral * 100} stroke="var(--tg-status-orange-fg)" strokeDasharray="4 4" />
                <ReferenceLine y={0} stroke="var(--tg-outline)" />
                <Bar dataKey="pct" radius={[6, 6, 0, 0]}>
                  {agregado.map((a) => (
                    <Cell key={a.componente} fill={Math.abs(a.variacion) > umbral ? 'var(--tg-status-red-fg)' : 'var(--tg-primary)'} />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </Tarjeta>
      )}

      {data && data.ordenes.some((o) => o.fueraDeUmbral) && (
        <Aviso tipo="advertencia" className="mb-4">
          {data.ordenes.filter((o) => o.fueraDeUmbral).length} órdenes tienen componentes fuera del umbral.
        </Aviso>
      )}

      <TablaDatos
        columnas={columnas}
        datos={data?.ordenes}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(o) => o.ordenId}
        mensajeVacio="No hay órdenes terminadas en el periodo."
        detalle={(o) => (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted">
                <th className="py-1 font-medium">Componente</th>
                <th className="py-1 text-right font-medium">Teórico</th>
                <th className="py-1 text-right font-medium">Real</th>
                <th className="py-1 text-right font-medium">Diferencia</th>
                <th className="py-1 text-right font-medium">Variación</th>
              </tr>
            </thead>
            <tbody>
              {o.componentes.map((c) => (
                <tr key={c.productoId} className="border-t border-row-divider">
                  <td className="py-1.5">{c.producto}</td>
                  <td className="py-1.5 text-right tabular-nums">
                    {formatoCantidad(c.teoricoAjustado)} {c.unidad}
                  </td>
                  <td className="py-1.5 text-right tabular-nums">
                    {formatoCantidad(c.real)} {c.unidad}
                  </td>
                  <td className="py-1.5 text-right tabular-nums">{formatoCantidad(c.diferencia)}</td>
                  <td className={cn('py-1.5 text-right font-medium tabular-nums', c.fueraDeUmbral && 'text-[var(--tg-status-red-fg)]')}>
                    {c.variacionPct != null ? formatoPorcentaje(c.variacionPct, 1) : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      />
    </Pagina>
  );
}
