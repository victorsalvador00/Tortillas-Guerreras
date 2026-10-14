import { useNavigate, useParams } from '@tanstack/react-router';
import {
  mensajeError,
  useCancelarVenta,
  useListarIncidencias,
  useListarVentas,
  useObtenerCliente,
  useObtenerListaPrecio,
  useObtenerVenta,
  useRegistrarVenta,
  type ClienteDto,
  type CondicionPago,
  type EstadoVenta,
  type FormaPago,
  type OrigenVenta,
  type VentaDto,
  type VentaPartidaDto,
} from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda, formatoPorcentaje, sumarDias } from '@tg/shared';
import {
  AreaTexto,
  Aviso,
  BarraAccion,
  Boton,
  Campo,
  Dato,
  Datos,
  DialogoConfirmacion,
  EtiquetaEstado,
  Selector,
  TablaDatos,
  Tarjeta,
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros, SelectorAlmacen, exportarCsv } from '@/componentes/comunes';
import { MapaLeaflet, type MarcadorMapa } from '@/componentes/MapaLeaflet';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { EditorPartidasVenta, ResumenTotales } from './EditorPartidasVenta';
import { calcularLineas, nuevaLinea, type LineaVenta } from './partidas';
import { ETIQUETAS_FORMA_PAGO, ETIQUETAS_INCIDENCIA, SelectorCliente, SelectorRuta, useRutas } from './selectores';

const centavos = (pesos: number) => Math.round(pesos * 100);

export function Ventas() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const rutas = useRutas();
  const hoy = fechaOperativa();
  const [desde, setDesde] = useState(hoy);
  const [hasta, setHasta] = useState(hoy);
  const [rutaId, setRutaId] = useState('');
  const [estado, setEstado] = useState('');
  const [origen, setOrigen] = useState('');
  const [revision, setRevision] = useState('');
  const [folio, setFolio] = useState('');
  const [pagina, setPagina] = useState(1);
  const { data, isLoading, isFetching } = useListarVentas({
    desde: desde || undefined,
    hasta: hasta || undefined,
    rutaId: rutaId || undefined,
    estado: (estado || undefined) as EstadoVenta | undefined,
    origen: (origen || undefined) as OrigenVenta | undefined,
    requiereRevision: revision === '' ? undefined : revision === 'true',
    folio: folio || undefined,
    pagina,
    tamano: 50,
  });
  const reiniciar = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    setPagina(1);
  };

  const columnas = useMemo<ColumnDef<VentaDto, unknown>[]>(
    () => [
      {
        header: 'Folio',
        accessorKey: 'folio',
        cell: ({ row }) => (
          <span className="flex items-center gap-1.5 font-medium">
            {row.original.folio}
            {row.original.requiereRevision && <span className="icono text-[18px] text-[var(--tg-status-orange-fg)]" title="Requiere revisión">flag</span>}
          </span>
        ),
      },
      { header: 'Hora', accessorKey: 'fechaHoraServidor', cell: ({ row }) => formatoFechaHora(row.original.fechaHoraDispositivo ?? row.original.fechaHoraServidor), meta: { ocultarEnMovil: true } },
      { header: 'Cliente', accessorKey: 'cliente' },
      { header: 'Ruta', id: 'ruta', cell: ({ row }) => rutas.find((r) => r.rutaId === row.original.rutaId)?.codigo ?? '—', meta: { ocultarEnMovil: true } },
      { header: 'Origen', accessorKey: 'origen', meta: { ocultarEnMovil: true } },
      { header: 'Pago', accessorKey: 'formaPago', cell: ({ row }) => ETIQUETAS_FORMA_PAGO[row.original.formaPago] ?? row.original.formaPago, meta: { ocultarEnMovil: true } },
      { header: 'Total', accessorKey: 'total', cell: ({ row }) => formatoMoneda(row.original.total), meta: { alinear: 'fin' } },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [rutas],
  );
  const total = (data?.items ?? []).filter((v) => v.estado === 'Registrada').reduce((s, v) => s + v.total, 0);

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Ventas"
        subtitulo="Ventas en ruta (app) y de mostrador (web). Los importes los recalcula el servidor (RN-VEN-02)."
        acciones={
          <>
            <Boton
              variante="secundario"
              icono="download"
              disabled={!data?.items.length}
              onClick={() =>
                exportarCsv('ventas', data?.items ?? [], [
                  { titulo: 'Folio', valor: (v) => v.folio },
                  { titulo: 'Fecha operativa', valor: (v) => v.fechaOperativa },
                  { titulo: 'Cliente', valor: (v) => v.cliente },
                  { titulo: 'Origen', valor: (v) => v.origen },
                  { titulo: 'Condición', valor: (v) => v.condicionPago },
                  { titulo: 'Forma de pago', valor: (v) => v.formaPago },
                  { titulo: 'Subtotal', valor: (v) => v.subtotal },
                  { titulo: 'IEPS', valor: (v) => v.ieps },
                  { titulo: 'IVA', valor: (v) => v.iva },
                  { titulo: 'Total', valor: (v) => v.total },
                  { titulo: 'Estado', valor: (v) => v.estado },
                  { titulo: 'Requiere revisión', valor: (v) => (v.requiereRevision ? 'Sí' : 'No') },
                ])
              }
            >
              Exportar CSV
            </Boton>
            {tienePermiso('ventas.registrar') && (
              <Boton icono="add" onClick={() => navegar({ to: '/ventas/ventas/nueva' })}>
                Nueva venta
              </Boton>
            )}
          </>
        }
      />
      <BarraFiltros>
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => reiniciar(setDesde)(e.target.value)} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => reiniciar(setHasta)(e.target.value)} />
        <SelectorRuta vacio="Todas" value={rutaId} onChange={reiniciar(setRutaId)} />
        <Campo etiqueta="Folio" type="search" value={folio} onChange={(e) => reiniciar(setFolio)(e.target.value)} />
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={[
            { valor: 'Registrada', etiqueta: 'Registrada' },
            { valor: 'Cancelada', etiqueta: 'Cancelada' },
          ]}
          value={estado}
          onChange={(e) => reiniciar(setEstado)(e.target.value)}
        />
        <Selector
          etiqueta="Origen"
          vacio="Todos"
          opciones={[
            { valor: 'App', etiqueta: 'App de ruta' },
            { valor: 'Web', etiqueta: 'Web' },
          ]}
          value={origen}
          onChange={(e) => reiniciar(setOrigen)(e.target.value)}
        />
        <Selector
          etiqueta="Revisión"
          vacio="Todas"
          opciones={[
            { valor: 'true', etiqueta: 'Requieren revisión' },
            { valor: 'false', etiqueta: 'Sin revisión' },
          ]}
          value={revision}
          onChange={(e) => reiniciar(setRevision)(e.target.value)}
        />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(v) => v.id}
        alClicFila={(v) => navegar({ to: '/ventas/ventas/$id', params: { id: v.id } })}
        mensajeVacio="No hay ventas con esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
      {data && data.items.length > 0 && (
        <p className="m-0 mt-2 px-2 text-right text-sm text-muted">
          Total registrado en esta página: <strong className="text-on-surface">{formatoMoneda(total)}</strong>
        </p>
      )}
    </Pagina>
  );
}

export function VentaDetalle() {
  const { id } = useParams({ strict: false }) as { id: string };
  const { tienePermiso } = useSesion();
  const consulta = useObtenerVenta(id);
  const v = consulta.data;
  const cliente = useObtenerCliente(v?.clienteId ?? '', { query: { enabled: !!v } });
  const incidencias = useListarIncidencias({ entidadId: id, tamano: 50 }, { query: { enabled: tienePermiso('ventas.supervisar') } });
  const cancelar = useCancelarVenta();
  const [cancelando, setCancelando] = useState(false);
  const [motivo, setMotivo] = useState('');
  const rutas = useRutas();

  const columnas = useMemo<ColumnDef<VentaPartidaDto, unknown>[]>(
    () => [
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.producto}</span>
            <span className="text-xs text-muted">
              {row.original.sku}
              {row.original.lote ? ` · Lote ${row.original.lote}` : ''}
            </span>
          </span>
        ),
      },
      { header: 'Cantidad', accessorKey: 'cantidad', cell: ({ row }) => `${formatoCantidad(row.original.cantidad)} ${row.original.unidad}`, meta: { alinear: 'fin' } },
      { header: 'Precio', accessorKey: 'precioUnitario', cell: ({ row }) => formatoMoneda(row.original.precioUnitario), meta: { alinear: 'fin' } },
      {
        header: 'Impuestos',
        id: 'tasas',
        cell: ({ row }) => `IVA ${formatoPorcentaje(row.original.tasaIva)}${row.original.tasaIeps ? ` · IEPS ${formatoPorcentaje(row.original.tasaIeps)}` : ''}`,
        meta: { ocultarEnMovil: true },
      },
      { header: 'Importe', accessorKey: 'importe', cell: ({ row }) => formatoMoneda(row.original.importe), meta: { alinear: 'fin' } },
      { header: 'IEPS', accessorKey: 'ieps', cell: ({ row }) => formatoMoneda(row.original.ieps), meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'IVA', accessorKey: 'iva', cell: ({ row }) => formatoMoneda(row.original.iva), meta: { alinear: 'fin', ocultarEnMovil: true } },
    ],
    [],
  );

  const marcadores: MarcadorMapa[] = [];
  if (v?.latitud != null && v.longitud != null) {
    marcadores.push({ id: 'venta', lat: v.latitud, lng: v.longitud, titulo: `Venta ${v.folio}`, detalle: v.precisionM != null ? `Precisión ±${v.precisionM} m` : undefined, color: v.requiereRevision ? 'rojo' : 'verde' });
  }
  const c = cliente.data;
  if (c?.latitud != null && c.longitud != null) {
    marcadores.push({ id: 'cliente', lat: c.latitud, lng: c.longitud, titulo: c.nombre, detalle: c.ubicacionAprobada ? 'Ubicación aprobada' : 'Ubicación registrada', color: 'azul' });
  }
  const lineas = marcadores.length === 2 ? [{ puntos: marcadores.map((m) => [m.lat, m.lng] as [number, number]), color: 'gris' as const, discontinua: true }] : [];
  const puedeCancelar = !!v && v.estado === 'Registrada' && !v.facturaId && v.fechaOperativa === fechaOperativa() && tienePermiso('ventas.cancelar');

  return (
    <Pagina>
      <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
        {v && (
          <>
            <EncabezadoPagina
              titulo={`Venta ${v.folio}`}
              subtitulo={`${v.cliente} · ${formatoFecha(v.fechaOperativa)} · ${v.origen === 'App' ? 'App de ruta' : 'Web'}`}
              migas={[{ etiqueta: 'Ventas' }, { etiqueta: 'Ventas', a: '/ventas/ventas' }, { etiqueta: v.folio }]}
              acciones={
                puedeCancelar && (
                  <Boton variante="secundario" icono="cancel" onClick={() => setCancelando(true)}>
                    Cancelar venta
                  </Boton>
                )
              }
            />
            <div className="flex flex-col gap-4">
              {v.requiereRevision && (
                <Aviso tipo="advertencia" titulo="Requiere revisión">
                  El GPS de la venta quedó lejos de la ubicación aprobada del cliente (RN-VEN-05).
                </Aviso>
              )}
              {v.estado === 'Cancelada' && <Aviso tipo="error" titulo="Venta cancelada">{v.motivoCancelacion ?? 'Sin motivo.'}</Aviso>}
              <Tarjeta>
                <Datos>
                  <Dato etiqueta="Estado"><EtiquetaEstado estado={v.estado} /></Dato>
                  <Dato etiqueta="Tipo">{v.tipo === 'EntregaPedido' ? 'Entrega de pedido' : 'Venta en ruta'}</Dato>
                  <Dato etiqueta="Ruta">{rutas.find((r) => r.rutaId === v.rutaId)?.codigo ?? '—'}</Dato>
                  <Dato etiqueta="Condición">{v.condicionPago === 'Credito' ? 'Crédito' : 'Contado'}</Dato>
                  <Dato etiqueta="Forma de pago">{ETIQUETAS_FORMA_PAGO[v.formaPago] ?? v.formaPago}</Dato>
                  <Dato etiqueta="Hora en el dispositivo">{formatoFechaHora(v.fechaHoraDispositivo)}</Dato>
                  <Dato etiqueta="Recibida en el servidor">{formatoFechaHora(v.fechaHoraServidor)}</Dato>
                  <Dato etiqueta="Factura">{v.facturaId ? 'Facturada' : 'Sin facturar'}</Dato>
                  {v.totalApp != null && v.totalApp !== v.total && (
                    <Dato etiqueta="Total calculado en la app">{formatoMoneda(v.totalApp)}</Dato>
                  )}
                </Datos>
              </Tarjeta>
              <TablaDatos columnas={columnas} datos={v.partidas} idFila={(p) => p.id} ordenable={false} />
              <Tarjeta>
                <ResumenTotales totales={{ subtotal: centavos(v.subtotal), ieps: centavos(v.ieps), iva: centavos(v.iva), total: centavos(v.total) }} />
              </Tarjeta>
              <Tarjeta
                titulo="Ubicación de la venta"
                subtitulo={
                  v.latitud == null
                    ? 'La venta se registró sin GPS.'
                    : `Distancia al cliente: ${v.distanciaClienteM != null ? `${formatoCantidad(v.distanciaClienteM, 0)} m` : 'sin ubicación aprobada'} · precisión ${v.precisionM != null ? `±${formatoCantidad(v.precisionM, 0)} m` : '—'}`
                }
              >
                {marcadores.length > 0 ? <MapaLeaflet marcadores={marcadores} lineas={lineas} /> : <p className="m-0 text-sm text-muted">Sin coordenadas.</p>}
              </Tarjeta>
              {incidencias.data && incidencias.data.items.length > 0 && (
                <Tarjeta titulo="Incidencias">
                  <ul className="m-0 flex list-none flex-col gap-2 p-0">
                    {incidencias.data.items.map((i) => (
                      <li key={i.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-row-divider px-3 py-2">
                        <span>{ETIQUETAS_INCIDENCIA[i.tipo] ?? i.tipo}</span>
                        <EtiquetaEstado estado={i.estado} />
                      </li>
                    ))}
                  </ul>
                </Tarjeta>
              )}
            </div>
            <DialogoConfirmacion
              abierto={cancelando}
              onCambiar={setCancelando}
              titulo={`Cancelar venta ${v.folio}`}
              mensaje="Se registra el movimiento contrario de inventario (RN-VEN-07). Solo el mismo día operativo y si no está facturada."
              textoConfirmar="Cancelar venta"
              peligro
              alConfirmar={async () => {
                if (!motivo.trim()) throw new Error('Escribe el motivo de la cancelación.');
                await cancelar.mutateAsync({ id: v.id, data: { motivo: motivo.trim() } });
                toast.success('Venta cancelada.');
              }}
            >
              <AreaTexto etiqueta="Motivo" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
            </DialogoConfirmacion>
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}

/** Venta de mostrador o captura posterior en la web [SUP-14]; precio obligatorio de la lista (RN-VEN-01). */
export function NuevaVenta() {
  const navegar = useNavigate();
  const registrar = useRegistrarVenta();
  const [cliente, setCliente] = useState<ClienteDto | null>(null);
  const [almacenId, setAlmacenId] = useState('');
  const [condicion, setCondicion] = useState<CondicionPago>('Contado');
  const [forma, setForma] = useState<FormaPago>('Efectivo');
  const [fecha, setFecha] = useState(fechaOperativa());
  const [lineas, setLineas] = useState<LineaVenta[]>([nuevaLinea()]);
  const [error, setError] = useState<string | null>(null);
  const lista = useObtenerListaPrecio(cliente?.listaPrecioId ?? '', { query: { enabled: !!cliente } });
  const calculo = calcularLineas(lineas, lista.data?.partidas);
  const rutas = useRutas();

  const guardar = async () => {
    setError(null);
    if (!cliente) return setError('Elige el cliente.');
    if (!almacenId) return setError('Elige el almacén de donde sale el producto.');
    if (!calculo.completas) return setError('Completa todas las partidas con producto, unidad, cantidad y precio de lista.');
    try {
      const venta = await registrar.mutateAsync({
        data: {
          clienteId: cliente.id,
          almacenId,
          condicionPago: condicion,
          formaPago: condicion === 'Credito' ? 'Credito' : forma,
          fechaOperativa: fecha || null,
          rutaId: rutas.find((r) => r.almacenId === almacenId)?.rutaId ?? null,
          partidas: lineas.map((l) => ({ productoId: l.producto!.id, unidadId: l.unidadId, cantidad: Number(l.cantidad) })),
        },
      });
      toast.success(`Venta ${venta.folio} registrada.`);
      await navegar({ to: '/ventas/ventas/$id', params: { id: venta.id } });
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <Pagina ancho="medio">
      <EncabezadoPagina
        titulo="Nueva venta"
        subtitulo="Venta de mostrador o captura posterior. Descuenta del almacén elegido."
        migas={[{ etiqueta: 'Ventas' }, { etiqueta: 'Ventas', a: '/ventas/ventas' }, { etiqueta: 'Nueva' }]}
      />
      <div className="flex flex-col gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Tarjeta>
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectorCliente
              valor={cliente?.id}
              cliente={cliente}
              className="sm:col-span-2"
              onCambiar={(c) => {
                setCliente(c);
                if (c) setCondicion(c.condicionPago);
              }}
            />
            <SelectorAlmacen etiqueta="Almacén de salida" vacio="Elige el almacén" incluirRutas value={almacenId} onChange={setAlmacenId} />
            <Campo
              etiqueta="Fecha operativa"
              type="date"
              value={fecha}
              max={fechaOperativa()}
              min={sumarDias(fechaOperativa(), -31)}
              onChange={(e) => setFecha(e.target.value)}
            />
            <Selector
              etiqueta="Condición de pago"
              opciones={[
                { valor: 'Contado', etiqueta: 'Contado' },
                { valor: 'Credito', etiqueta: 'Crédito', deshabilitada: cliente?.condicionPago !== 'Credito' },
              ]}
              value={condicion}
              onChange={(e) => setCondicion(e.target.value as CondicionPago)}
              ayuda={cliente?.condicionPago === 'Credito' ? 'El cliente tiene crédito (RN-VEN-04).' : undefined}
            />
            <Selector
              etiqueta="Forma de pago"
              opciones={[
                { valor: 'Efectivo', etiqueta: 'Efectivo' },
                { valor: 'Transferencia', etiqueta: 'Transferencia' },
              ]}
              value={condicion === 'Credito' ? 'Efectivo' : forma}
              disabled={condicion === 'Credito'}
              onChange={(e) => setForma(e.target.value as FormaPago)}
            />
          </div>
        </Tarjeta>
        <Tarjeta titulo="Partidas">
          {!cliente ? (
            <p className="m-0 text-sm text-muted">Elige el cliente para usar su lista de precios.</p>
          ) : (
            <EditorPartidasVenta lineas={lineas} onCambiar={setLineas} precios={lista.data?.partidas} />
          )}
        </Tarjeta>
        <BarraAccion resumen={<span>Total <strong>{formatoMoneda(calculo.totales.total / 100)}</strong></span>}>
          <Boton variante="texto" onClick={() => navegar({ to: '/ventas/ventas' })}>
            Cancelar
          </Boton>
          <Boton cargando={registrar.isPending} onClick={guardar}>
            Registrar venta
          </Boton>
        </BarraAccion>
      </div>
    </Pagina>
  );
}
