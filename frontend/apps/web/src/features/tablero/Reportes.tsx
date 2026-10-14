import { useReporteContinuidad, useReporteMermas, useReporteVentas, type VisitaClienteDto } from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoMoneda, formatoPorcentaje, sumarDias } from '@tg/shared';
import { Boton, Campo, EtiquetaEstado, Pestanas, TablaDatos, Tarjeta, type ColumnDef } from '@tg/ui';
import { useMemo, useState, type ReactNode } from 'react';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { exportarCsv } from '@/componentes/comunes';
import { GraficaBarras, formatoPesos } from './graficas';

function Periodo({ desde, hasta, onDesde, onHasta }: { desde: string; hasta: string; onDesde: (v: string) => void; onHasta: (v: string) => void }) {
  return (
    <div className="mb-4 grid max-w-xl grid-cols-2 gap-3">
      <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => e.target.value && onDesde(e.target.value)} />
      <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => e.target.value && onHasta(e.target.value)} />
    </div>
  );
}

function ReporteMermas() {
  const [desde, setDesde] = useState(sumarDias(fechaOperativa(), -30));
  const [hasta, setHasta] = useState(fechaOperativa());
  const { data, isLoading, error } = useReporteMermas({ desde, hasta });
  return (
    <>
      <Periodo desde={desde} hasta={hasta} onDesde={setDesde} onHasta={setHasta} />
      <EstadoConsulta cargando={isLoading} error={error}>
        {data && (
          <div className="grid gap-4 lg:grid-cols-2">
            <Tarjeta
              titulo="Mermas por motivo"
              subtitulo={`Merma del periodo: ${formatoMoneda(data.total.costoMerma)} · ${
                data.total.porcentaje != null ? formatoPorcentaje(data.total.porcentaje, 1) : '—'
              } del costo producido`}
            >
              <GraficaBarras
                descripcion="Costo de merma por motivo"
                datos={data.porMotivo.map((m) => ({ motivo: m.motivo, Costo: m.costo }))}
                categoria="motivo"
                series={[{ clave: 'Costo', nombre: 'Costo de merma' }]}
                formato={formatoPesos}
              />
            </Tarjeta>
            <Tarjeta
              titulo="Mermas por producto"
              acciones={
                <Boton
                  variante="secundario"
                  tamano="chico"
                  icono="download"
                  onClick={() =>
                    exportarCsv(`mermas-${desde}-${hasta}`, data.porProducto, [
                      { titulo: 'SKU', valor: (x) => x.sku },
                      { titulo: 'Producto', valor: (x) => x.producto },
                      { titulo: 'Cantidad', valor: (x) => x.cantidad },
                      { titulo: 'Unidad', valor: (x) => x.unidad },
                      { titulo: 'Costo', valor: (x) => x.costo },
                    ])
                  }
                >
                  CSV
                </Boton>
              }
            >
              <TablaDatos
                ordenable={false}
                datos={data.porProducto}
                mensajeVacio="Sin mermas en el periodo."
                columnas={[
                  { header: 'Producto', accessorKey: 'producto' },
                  { header: 'Cantidad', id: 'c', cell: ({ row }) => `${formatoCantidad(row.original.cantidad)} ${row.original.unidad}`, meta: { alinear: 'fin' } },
                  { header: 'Costo', id: 'k', cell: ({ row }) => formatoMoneda(row.original.costo), meta: { alinear: 'fin' } },
                ]}
              />
            </Tarjeta>
          </div>
        )}
      </EstadoConsulta>
    </>
  );
}

function ReporteVentas() {
  const [desde, setDesde] = useState(sumarDias(fechaOperativa(), -30));
  const [hasta, setHasta] = useState(fechaOperativa());
  const { data, isLoading, error } = useReporteVentas({ desde, hasta });
  return (
    <>
      <Periodo desde={desde} hasta={hasta} onDesde={setDesde} onHasta={setHasta} />
      <EstadoConsulta cargando={isLoading} error={error}>
        {data && (
          <div className="grid gap-4 lg:grid-cols-2">
            <Tarjeta titulo="Ventas por día" subtitulo={`Total del periodo: ${formatoMoneda(data.total)}`} className="lg:col-span-2">
              <GraficaBarras
                descripcion="Ventas por día"
                datos={data.porDia.map((d) => ({ dia: formatoFecha(d.fecha).slice(0, 5), Total: d.total }))}
                categoria="dia"
                series={[{ clave: 'Total', nombre: 'Ventas' }]}
                formato={formatoPesos}
              />
            </Tarjeta>
            <Tarjeta titulo="Por ruta">
              <GraficaBarras
                descripcion="Ventas por ruta"
                datos={data.porRuta.map((r) => ({ ruta: r.grupo, Total: r.total }))}
                categoria="ruta"
                series={[{ clave: 'Total', nombre: 'Ventas' }]}
                formato={formatoPesos}
              />
            </Tarjeta>
            <Tarjeta titulo="Por canal">
              <GraficaBarras
                descripcion="Ventas por canal"
                datos={data.porCanal.map((r) => ({ canal: r.grupo, Total: r.total }))}
                categoria="canal"
                series={[{ clave: 'Total', nombre: 'Ventas' }]}
                formato={formatoPesos}
              />
            </Tarjeta>
            <Tarjeta titulo="Por producto" className="lg:col-span-2">
              <TablaDatos
                datos={data.porProducto}
                mensajeVacio="Sin ventas en el periodo."
                columnas={[
                  { header: 'SKU', accessorKey: 'sku' },
                  { header: 'Producto', accessorKey: 'producto' },
                  { header: 'Cantidad', accessorKey: 'cantidad', cell: ({ row }) => `${formatoCantidad(row.original.cantidad)} ${row.original.unidad}`, meta: { alinear: 'fin' } },
                  { header: 'Importe (sin impuestos)', accessorKey: 'importe', cell: ({ row }) => formatoMoneda(row.original.importe), meta: { alinear: 'fin' } },
                ]}
              />
            </Tarjeta>
          </div>
        )}
      </EstadoConsulta>
    </>
  );
}

function ReporteContinuidad() {
  const [fecha, setFecha] = useState(fechaOperativa());
  const { data, isLoading, error } = useReporteContinuidad({ fecha });
  const columnas = useMemo<ColumnDef<VisitaClienteDto, unknown>[]>(
    () => [
      { header: 'Ruta', accessorKey: 'ruta' },
      { header: 'Orden', accessorKey: 'ordenVisita', meta: { alinear: 'fin' } },
      { header: 'Cliente', accessorKey: 'cliente' },
      { header: 'Visita', accessorKey: 'visitado', cell: ({ row }) => <EtiquetaEstado color={row.original.visitado ? 'verde' : 'naranja'} etiqueta={row.original.visitado ? 'Visitado' : 'Sin visita'} /> },
      { header: 'Vendido', accessorKey: 'vendido', cell: ({ row }) => formatoMoneda(row.original.vendido), meta: { alinear: 'fin' } },
    ],
    [],
  );
  return (
    <>
      <div className="mb-4 max-w-xs">
        <Campo etiqueta="Fecha operativa" type="date" value={fecha} onChange={(e) => e.target.value && setFecha(e.target.value)} />
      </div>
      <EstadoConsulta cargando={isLoading} error={error}>
        {data && (
          <div className="flex flex-col gap-4">
            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
              {data.rutas.map((r) => (
                <Tarjeta key={r.ruta}>
                  <p className="m-0 text-sm text-muted">Ruta {r.ruta}</p>
                  <p className="m-0 text-28 tabular-nums">{formatoPorcentaje(r.porcentaje, 0)}</p>
                  <p className="m-0 text-13 text-muted">
                    {r.visitados} de {r.programados} clientes programados
                  </p>
                </Tarjeta>
              ))}
            </div>
            <TablaDatos columnas={columnas} datos={data.clientes} mensajeVacio="No hay clientes programados para ese día." />
          </div>
        )}
      </EstadoConsulta>
    </>
  );
}

export function Reportes() {
  const { tienePermiso } = useSesion();
  const pestanas = [
    tienePermiso(['produccion.ver', 'inventario.ver', 'tablero.ver']) && { valor: 'mermas', etiqueta: 'Mermas', contenido: <ReporteMermas /> },
    tienePermiso(['ventas.ver', 'tablero.ver']) && { valor: 'ventas', etiqueta: 'Ventas', contenido: <ReporteVentas /> },
    tienePermiso(['ventas.ver', 'logistica.ver']) && { valor: 'continuidad', etiqueta: 'Continuidad de visitas', contenido: <ReporteContinuidad /> },
  ].filter(Boolean) as { valor: string; etiqueta: string; contenido: ReactNode }[];
  const [pestana, setPestana] = useState(pestanas[0]?.valor ?? 'mermas');
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Reportes"
        subtitulo="Mermas por motivo, ventas por ruta, canal y producto, y continuidad de visitas. La variación de producción está en Producción."
      />
      <Pestanas valor={pestana} onCambiar={setPestana} pestanas={pestanas} />
    </Pagina>
  );
}
