import { useNavigate } from '@tanstack/react-router';
import {
  esErrorApi,
  mensajeError,
  useListarOrdenesProduccion,
  useListarRecetas,
  usePlanearOrdenProduccion,
  type EstadoOrden,
  type OrdenDto,
  type ProductoDto,
  type Turno,
} from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoFechaLarga, formatoMoneda } from '@tg/shared';
import { Aviso, Boton, Campo, Dialogo, EstadoVacio, EtiquetaEstado, Selector, TablaDatos, cn, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros, SelectorAlmacen, SelectorProducto, exportarCsv } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { avanceOrden } from './calculos';
import { ESTADOS_ORDEN, SelectorLinea, TURNOS } from './comunes';

/** Planear una orden: producto, receta vigente, turno, línea y cantidad. */
export function DialogoPlanear({ abierto, onCambiar, fecha }: { abierto: boolean; onCambiar: (a: boolean) => void; fecha: string }) {
  const navegar = useNavigate();
  const planear = usePlanearOrdenProduccion();
  const [producto, setProducto] = useState<ProductoDto | null>(null);
  const [recetaId, setRecetaId] = useState('');
  const [turno, setTurno] = useState<Turno>('Matutino');
  const [lineaId, setLineaId] = useState('');
  const [cantidad, setCantidad] = useState('');
  const [dia, setDia] = useState(fecha);
  const [consumo, setConsumo] = useState('');
  const [destino, setDestino] = useState('');
  const [notas, setNotas] = useState('');
  const [errores, setErrores] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const recetas = useListarRecetas({ productoId: producto?.id }, { query: { enabled: !!producto } });
  const disponibles = (recetas.data ?? []).filter((r) => r.estado !== 'Obsoleta');
  const vigente = disponibles.find((r) => r.estado === 'Vigente');

  const enviar = async () => {
    setError(null);
    const e: Record<string, string> = {};
    if (!producto) e.productoId = 'Elige el producto.';
    if (!(Number(cantidad) > 0)) e.cantidadPlaneada = 'Captura la cantidad a producir.';
    if (producto && recetas.data && !vigente && !recetaId) e.recetaId = 'El producto no tiene receta vigente.';
    setErrores(e);
    if (Object.keys(e).length) return;
    try {
      const orden = await planear.mutateAsync({
        data: {
          productoId: producto!.id,
          recetaId: recetaId || null,
          fechaOperativa: dia || null,
          turno,
          lineaId: lineaId || null,
          cantidadPlaneada: Number(cantidad),
          almacenConsumoId: consumo || null,
          almacenDestinoId: destino || null,
          notas: notas || null,
        },
      });
      toast.success(`Orden ${orden.folio} planeada.`);
      onCambiar(false);
      await navegar({ to: '/produccion/ordenes/$id', params: { id: orden.id } });
    } catch (err) {
      if (esErrorApi(err)) setErrores(Object.fromEntries(Object.entries(err.errores).map(([c, m]) => [c, m[0]])));
      setError(mensajeError(err));
    }
  };

  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo="Planear orden de producción"
      descripcion="Los componentes salen del almacén que indica la receta; los que no tienen, del almacén de consumo (Materias primas por omisión). El terminado entra a Producto terminado y el semiterminado a Materias primas."
      acciones={
        <>
          <Boton variante="texto" onClick={() => onCambiar(false)}>
            Cancelar
          </Boton>
          <Boton cargando={planear.isPending} onClick={enviar}>
            Planear
          </Boton>
        </>
      }
    >
      <div className="grid gap-4 sm:grid-cols-2">
        {error && (
          <Aviso tipo="error" className="sm:col-span-2">
            {error}
          </Aviso>
        )}
        <SelectorProducto
          etiqueta="Producto"
          className="sm:col-span-2"
          valor={producto?.id}
          producto={producto}
          filtro={{ esProducible: true }}
          onCambiar={(p) => {
            setProducto(p);
            setRecetaId('');
          }}
          error={errores.productoId}
        />
        <Selector
          etiqueta="Receta"
          vacio={vigente ? `Vigente: v${vigente.version}` : 'Sin receta vigente'}
          opciones={disponibles.filter((r) => r.estado !== 'Vigente').map((r) => ({ valor: r.id, etiqueta: `v${r.version} (${r.estado})` }))}
          value={recetaId}
          onChange={(e) => setRecetaId(e.target.value)}
          error={errores.recetaId}
          ayuda={vigente ? `Rinde ${formatoCantidad(vigente.rendimientoCantidad)} ${vigente.unidadBase}` : undefined}
        />
        <Campo
          etiqueta={`Cantidad (${producto?.unidadBaseClave ?? 'unidad base'})`}
          type="number"
          step="any"
          inputMode="decimal"
          value={cantidad}
          onChange={(e) => setCantidad(e.target.value)}
          error={errores.cantidadPlaneada}
        />
        <Campo etiqueta="Fecha operativa" type="date" value={dia} onChange={(e) => setDia(e.target.value)} />
        <Selector etiqueta="Turno" opciones={TURNOS} value={turno} onChange={(e) => setTurno(e.target.value as Turno)} />
        <SelectorLinea value={lineaId} onChange={setLineaId} />
        <SelectorAlmacen etiqueta="Consume de" vacio="Predeterminado (MP)" value={consumo} onChange={setConsumo} />
        <SelectorAlmacen etiqueta="Entra a" vacio="Predeterminado" value={destino} onChange={setDestino} />
        <Campo etiqueta="Notas" className="sm:col-span-2" value={notas} onChange={(e) => setNotas(e.target.value)} />
      </div>
    </Dialogo>
  );
}

function TarjetaOrden({ orden, alAbrir }: { orden: OrdenDto; alAbrir: () => void }) {
  const avance = avanceOrden(orden.estado, orden.cantidadPlaneada, orden.cantidadProducida);
  return (
    <button
      type="button"
      onClick={alAbrir}
      className="flex w-full flex-col gap-2 rounded-card border border-card-border bg-card p-4 text-left shadow-card transition-colors hover:bg-row-hover"
    >
      <span className="flex items-start justify-between gap-2">
        <span className="min-w-0">
          <span className="block font-medium">{orden.producto}</span>
          <span className="text-xs text-muted">
            {orden.folio}
            {orden.linea ? ` · ${orden.linea}` : ''}
          </span>
        </span>
        <EtiquetaEstado estado={orden.estado} />
      </span>
      <span className="text-sm tabular-nums">
        {orden.estado === 'Terminada'
          ? `${formatoCantidad(orden.cantidadProducida)} de ${formatoCantidad(orden.cantidadPlaneada)} ${orden.unidad}`
          : `Plan ${formatoCantidad(orden.cantidadPlaneada)} ${orden.unidad}`}
      </span>
      <span className="h-1.5 w-full overflow-hidden rounded-full bg-container-high" aria-label={`Avance ${avance} %`}>
        <span
          className={cn('block h-full', orden.estado === 'Terminada' ? 'bg-[var(--tg-status-green-fg)]' : 'bg-primary')}
          style={{ width: `${avance}%` }}
        />
      </span>
    </button>
  );
}

/** Tablero del día por turno: tarjetas de órdenes con estado y avance. */
export function PlanDia() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const [fecha, setFecha] = useState(fechaOperativa());
  const [lineaId, setLineaId] = useState('');
  const [planear, setPlanear] = useState(false);
  const { data, isLoading } = useListarOrdenesProduccion({ fecha, lineaId: lineaId || undefined });
  const ordenes = (data ?? []).filter((o) => o.estado !== 'Cancelada');

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Plan del día"
        subtitulo={formatoFechaLarga(fecha)}
        acciones={
          tienePermiso('produccion.planear') && (
            <Boton icono="add" tamano="grande" onClick={() => setPlanear(true)}>
              Planear orden
            </Boton>
          )
        }
      />
      <BarraFiltros>
        <Campo etiqueta="Fecha operativa" type="date" value={fecha} onChange={(e) => setFecha(e.target.value || fechaOperativa())} />
        <SelectorLinea vacio="Todas las líneas" value={lineaId} onChange={setLineaId} />
      </BarraFiltros>
      {isLoading ? (
        <div className="grid gap-4 md:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <span key={i} className="tg-skeleton h-32" />
          ))}
        </div>
      ) : ordenes.length === 0 ? (
        <EstadoVacio icono="event_busy" mensaje="No hay órdenes planeadas para este día." />
      ) : (
        <div className="grid gap-4 md:grid-cols-3">
          {TURNOS.map((t) => {
            const delTurno = ordenes.filter((o) => o.turno === t.valor);
            const planeado = delTurno.reduce((s, o) => s + o.cantidadPlaneada, 0);
            return (
              <section key={t.valor} className="flex flex-col gap-3">
                <h2 className="m-0 flex items-baseline justify-between text-base font-medium">
                  {t.etiqueta}
                  <span className="text-xs font-normal text-muted">
                    {delTurno.length} órdenes · {formatoCantidad(planeado)} planeado
                  </span>
                </h2>
                {delTurno.length === 0 && <p className="m-0 rounded-card border border-dashed border-outline-variant p-4 text-sm text-muted">Sin órdenes.</p>}
                {delTurno.map((o) => (
                  <TarjetaOrden key={o.id} orden={o} alAbrir={() => navegar({ to: '/produccion/ordenes/$id', params: { id: o.id } })} />
                ))}
              </section>
            );
          })}
        </div>
      )}
      {planear && <DialogoPlanear abierto onCambiar={setPlanear} fecha={fecha} />}
    </Pagina>
  );
}

export function OrdenesProduccion() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const [desde, setDesde] = useState(fechaOperativa());
  const [hasta, setHasta] = useState(fechaOperativa());
  const [estado, setEstado] = useState('');
  const [turno, setTurno] = useState('');
  const [producto, setProducto] = useState<ProductoDto | null>(null);
  const [planear, setPlanear] = useState(false);
  const { data, isLoading, isFetching } = useListarOrdenesProduccion({
    desde: desde || undefined,
    hasta: hasta || undefined,
    estado: (estado || undefined) as EstadoOrden | undefined,
    turno: (turno || undefined) as Turno | undefined,
    productoId: producto?.id,
  });
  const columnas = useMemo<ColumnDef<OrdenDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Turno', accessorKey: 'turno', meta: { ocultarEnMovil: true } },
      { header: 'Producto', accessorKey: 'producto' },
      { header: 'Línea', accessorKey: 'linea', cell: ({ row }) => row.original.linea ?? '—', meta: { ocultarEnMovil: true } },
      {
        header: 'Planeado',
        accessorKey: 'cantidadPlaneada',
        cell: ({ row }) => `${formatoCantidad(row.original.cantidadPlaneada)} ${row.original.unidad}`,
        meta: { alinear: 'fin' },
      },
      {
        header: 'Producido',
        accessorKey: 'cantidadProducida',
        cell: ({ row }) => (row.original.cantidadProducida != null ? formatoCantidad(row.original.cantidadProducida) : '—'),
        meta: { alinear: 'fin' },
      },
      {
        header: 'Costo unitario',
        accessorKey: 'costoUnitario',
        cell: ({ row }) => formatoMoneda(row.original.costoUnitario),
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Órdenes de producción"
        subtitulo="Planeadas, liberadas, en proceso y terminadas con su lote y costo."
        acciones={
          <>
            <Boton
              variante="secundario"
              icono="download"
              disabled={!data?.length}
              onClick={() =>
                exportarCsv('ordenes-produccion', data ?? [], [
                  { titulo: 'Folio', valor: (o) => o.folio },
                  { titulo: 'Fecha', valor: (o) => o.fechaOperativa },
                  { titulo: 'Turno', valor: (o) => o.turno },
                  { titulo: 'Producto', valor: (o) => o.producto },
                  { titulo: 'Lote', valor: (o) => o.lote },
                  { titulo: 'Planeado', valor: (o) => o.cantidadPlaneada },
                  { titulo: 'Producido', valor: (o) => o.cantidadProducida ?? '' },
                  { titulo: 'Merma', valor: (o) => o.mermaTotal },
                  { titulo: 'Costo total', valor: (o) => o.costoTotal ?? '' },
                  { titulo: 'Estado', valor: (o) => o.estado },
                ])
              }
            >
              Exportar CSV
            </Boton>
            {tienePermiso('produccion.planear') && (
              <Boton icono="add" onClick={() => setPlanear(true)}>
                Planear orden
              </Boton>
            )}
          </>
        }
      />
      <BarraFiltros>
        <div className="grid grid-cols-2 gap-3">
          <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
          <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
        </div>
        <Selector etiqueta="Estado" vacio="Todos" opciones={ESTADOS_ORDEN} value={estado} onChange={(e) => setEstado(e.target.value)} />
        <Selector etiqueta="Turno" vacio="Todos" opciones={TURNOS} value={turno} onChange={(e) => setTurno(e.target.value)} />
        <SelectorProducto etiqueta="Producto" valor={producto?.id} producto={producto} filtro={{ esProducible: true }} onCambiar={setProducto} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(o) => o.id}
        alClicFila={(o) => navegar({ to: '/produccion/ordenes/$id', params: { id: o.id } })}
        mensajeVacio="No hay órdenes con esos filtros."
      />
      {planear && <DialogoPlanear abierto onCambiar={setPlanear} fecha={fechaOperativa()} />}
    </Pagina>
  );
}
