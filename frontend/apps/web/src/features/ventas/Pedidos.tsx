import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import {
  mensajeError,
  obtenerProducto,
  useActualizarPedido,
  useAsignarPedido,
  useCancelarPedido,
  useConfirmarPedido,
  useCrearPedido,
  useListarPedidos,
  useObtenerCliente,
  useObtenerListaPrecio,
  useListarPreciosCliente,
  useListarVentas,
  useObtenerPedido,
  type ClienteDto,
  type EstadoPedido,
  type ListarPedidosParams,
  type PedidoDto,
  type PedidoPartidaDto,
  type VentaDto,
} from '@tg/api-client';
import { formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda, sumarDias, fechaOperativa } from '@tg/shared';
import {
  Aviso,
  BarraAccion,
  Boton,
  Campo,
  Dato,
  Datos,
  DialogoConfirmacion,
  EtiquetaEstado,
  Pestanas,
  Selector,
  TablaDatos,
  Tarjeta,
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros, Contador, exportarCsv } from '@/componentes/comunes';
import { HistorialDocumento } from '@/componentes/historial';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { EditorPartidasVenta, ResumenTotales } from './EditorPartidasVenta';
import { calcularLineas, combinarPrecios, nuevaLinea, type LineaVenta } from './partidas';
import { SelectorCliente, SelectorRuta, useRutas } from './selectores';

const ETIQUETA_ORIGEN: Record<string, string> = { App: 'App de ruta', Web: 'Web' };

const ESTADOS: EstadoPedido[] = ['Capturado', 'Confirmado', 'Asignado', 'Entregado', 'EntregadoParcial', 'Cancelado'];

/** Lo que espera en cada pestaña; «todos» usa los filtros de estado y fechas. */
const FILTRO_PESTANA = {
  // RN-VEN-18: "Por validar" = pedidos de la app todavía Capturado.
  porValidar: { estado: 'Capturado', origen: 'App' },
  porAsignar: { estado: 'Confirmado' },
  porCargar: { estado: 'Asignado', sinCarga: true },
} as const satisfies Record<string, ListarPedidosParams>;

const VACIO_PESTANA: Record<string, string> = {
  todos: 'No hay pedidos con esos filtros.',
  porValidar: 'No hay pedidos de la app por validar.',
  porAsignar: 'No hay pedidos confirmados sin ruta.',
  porCargar: 'No hay pedidos asignados pendientes de cargar.',
};

export function Pedidos() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const rutas = useRutas();
  const [pestana, setPestana] = useState('todos');
  const [estado, setEstado] = useState('');
  const [rutaId, setRutaId] = useState('');
  const [desde, setDesde] = useState(sumarDias(fechaOperativa(), -30));
  const [hasta, setHasta] = useState('');
  const [pagina, setPagina] = useState(1);
  const todos = pestana === 'todos';
  // Lo pendiente lo es sin importar la fecha: las pestañas no filtran por fechas.
  const { data, isLoading, isFetching } = useListarPedidos({
    ...(todos
      ? { estado: (estado || undefined) as EstadoPedido | undefined, desde: desde || undefined, hasta: hasta || undefined }
      : FILTRO_PESTANA[pestana as keyof typeof FILTRO_PESTANA]),
    rutaId: rutaId || undefined,
    pagina,
    tamano: 50,
  });
  // Los contadores muestran todo lo pendiente, sin importar los filtros.
  const porValidar = useListarPedidos({ ...FILTRO_PESTANA.porValidar, tamano: 1 });
  const porAsignar = useListarPedidos({ ...FILTRO_PESTANA.porAsignar, tamano: 1 });
  const porCargar = useListarPedidos({ ...FILTRO_PESTANA.porCargar, tamano: 1 });
  const cambiarPagina = <T,>(fijar: (v: T) => void) => (v: T) => {
    fijar(v);
    setPagina(1);
  };
  const nombreRuta = (id?: string | null) => rutas.find((r) => r.rutaId === id)?.codigo ?? '—';

  const columnas = useMemo<ColumnDef<PedidoDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      {
        header: 'Cliente',
        accessorKey: 'cliente',
        cell: ({ row }) => (
          <span>
            {row.original.cliente}
            {row.original.esRuta && <span className="ml-2 text-xs text-muted">Pedido de ruta</span>}
          </span>
        ),
      },
      { header: 'Origen', accessorKey: 'origen', cell: ({ row }) => ETIQUETA_ORIGEN[row.original.origen], meta: { ocultarEnMovil: true } },
      { header: 'Fecha', accessorKey: 'fecha', cell: ({ row }) => formatoFecha(row.original.fecha), meta: { ocultarEnMovil: true } },
      { header: 'Entrega', accessorKey: 'fechaEntrega', cell: ({ row }) => formatoFecha(row.original.fechaEntrega), meta: { ocultarEnMovil: true } },
      { header: 'Ruta', accessorKey: 'rutaId', cell: ({ row }) => nombreRuta(row.original.rutaId), meta: { ocultarEnMovil: true } },
      { header: 'Total', accessorKey: 'total', cell: ({ row }) => formatoMoneda(row.original.total), meta: { alinear: 'fin' } },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [rutas],
  );

  const contenido = (
    <>
      <BarraFiltros>
        {todos && (
          <Selector
            etiqueta="Estado"
            vacio="Todos"
            opciones={ESTADOS.map((e) => ({ valor: e, etiqueta: e === 'EntregadoParcial' ? 'Entregado parcial' : e }))}
            value={estado}
            onChange={(e) => cambiarPagina(setEstado)(e.target.value)}
          />
        )}
        <SelectorRuta vacio="Todas" value={rutaId} onChange={cambiarPagina(setRutaId)} />
        {todos && (
          <>
            <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => cambiarPagina(setDesde)(e.target.value)} />
            <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => cambiarPagina(setHasta)(e.target.value)} />
          </>
        )}
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(p) => p.id}
        alClicFila={(p) => navegar({ to: '/ventas/pedidos/$id', params: { id: p.id } })}
        mensajeVacio={VACIO_PESTANA[pestana]}
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </>
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Pedidos"
        subtitulo="Pedidos institucionales y de autoservicio: captura, confirmación y asignación a ruta."
        acciones={
          <>
            <Boton
              variante="secundario"
              icono="download"
              disabled={!data?.items.length}
              onClick={() =>
                exportarCsv('pedidos', data?.items ?? [], [
                  { titulo: 'Folio', valor: (p) => p.folio },
                  { titulo: 'Cliente', valor: (p) => p.cliente },
                  { titulo: 'Fecha', valor: (p) => p.fecha },
                  { titulo: 'Entrega', valor: (p) => p.fechaEntrega ?? '' },
                  { titulo: 'Estado', valor: (p) => p.estado },
                  { titulo: 'Total', valor: (p) => p.total },
                ])
              }
            >
              Exportar CSV
            </Boton>
            {tienePermiso('ventas.pedidos') && (
              <Boton icono="add" onClick={() => navegar({ to: '/ventas/pedidos/nuevo' })}>
                Nuevo pedido
              </Boton>
            )}
          </>
        }
      />
      <Pestanas
        estirar
        valor={pestana}
        onCambiar={cambiarPagina(setPestana)}
        pestanas={[
          { valor: 'todos', etiqueta: 'Todos', contenido },
          {
            valor: 'porValidar',
            etiqueta: (
              <>
                Por validar
                <Contador valor={porValidar.data?.total} />
              </>
            ),
            contenido,
          },
          {
            valor: 'porAsignar',
            etiqueta: (
              <>
                Por asignar ruta
                <Contador valor={porAsignar.data?.total} />
              </>
            ),
            contenido,
          },
          {
            valor: 'porCargar',
            etiqueta: (
              <>
                Por cargar
                <Contador valor={porCargar.data?.total} />
              </>
            ),
            contenido,
          },
        ]}
      />
    </Pagina>
  );
}

/** Convierte las partidas guardadas en líneas editables (necesita los productos para unidades e impuestos). */
async function lineasDePedido(partidas: PedidoPartidaDto[]): Promise<LineaVenta[]> {
  const productos = await Promise.all([...new Set(partidas.map((p) => p.productoId))].map((id) => obtenerProducto(id)));
  return partidas.map((p) => ({
    ...nuevaLinea(),
    id: p.id,
    tipo: p.tipo,
    producto: productos.find((x) => x.id === p.productoId) ?? null,
    unidadId: p.unidadId,
    cantidad: String(p.cantidad),
  }));
}

export function PedidoFormulario() {
  const { id } = useParams({ strict: false }) as { id?: string };
  const consulta = useObtenerPedido(id ?? '', { query: { enabled: !!id } });
  const lineas = useQuery({
    queryKey: ['ventas', 'pedido-lineas', id],
    queryFn: () => lineasDePedido(consulta.data!.partidas),
    enabled: !!consulta.data,
    staleTime: Infinity,
  });
  return (
    <Pagina ancho="medio">
      <EstadoConsulta cargando={!!id && (consulta.isLoading || lineas.isLoading)} error={consulta.error ?? lineas.error}>
        <FormularioPedido key={consulta.data?.id ?? 'nuevo'} pedido={consulta.data} lineasIniciales={lineas.data} />
      </EstadoConsulta>
    </Pagina>
  );
}

function FormularioPedido({ pedido, lineasIniciales }: { pedido?: PedidoDto; lineasIniciales?: LineaVenta[] }) {
  const navegar = useNavigate();
  const crear = useCrearPedido();
  const actualizar = useActualizarPedido();
  const [clienteId, setClienteId] = useState(pedido?.clienteId ?? '');
  const [clienteSel, setClienteSel] = useState<Pick<ClienteDto, 'id' | 'codigo' | 'nombre'> | null>(
    pedido ? { id: pedido.clienteId, codigo: '', nombre: pedido.cliente } : null,
  );
  const [fechaEntrega, setFechaEntrega] = useState(pedido?.fechaEntrega ?? sumarDias(fechaOperativa(), 1));
  const [ordenCompra, setOrdenCompra] = useState(pedido?.ordenCompraCliente ?? '');
  const [notas, setNotas] = useState(pedido?.notas ?? '');
  const [lineas, setLineas] = useState<LineaVenta[]>(lineasIniciales ?? [nuevaLinea()]);
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const cliente = useObtenerCliente(clienteId, { query: { enabled: !!clienteId } });
  const lista = useObtenerListaPrecio(cliente.data?.listaPrecioId ?? '', { query: { enabled: !!cliente.data } });
  const especiales = useListarPreciosCliente(clienteId, { query: { enabled: !!clienteId } });
  const precios = combinarPrecios(lista.data?.partidas, especiales.data, fechaOperativa());
  const calculo = calcularLineas(lineas, precios);
  // RN-VEN-16: en canales por cliente se permite más de un pedido por fecha; solo se avisa.
  const mismos = useListarPedidos(
    { clienteId: clienteId || undefined, desde: fechaEntrega || undefined, hasta: fechaEntrega || undefined, tamano: 5 },
    { query: { enabled: !pedido && !!clienteId && !!fechaEntrega } },
  );
  const existente = mismos.data?.items.find((x) => x.estado !== 'Cancelado' && x.fechaEntrega === fechaEntrega);

  const guardar = async () => {
    setError(null);
    if (!clienteId) return setError('Elige el cliente.');
    if (!calculo.completas) return setError('Completa todas las partidas con producto, unidad, cantidad y precio de lista.');
    setEnviando(true);
    try {
      const data = {
        clienteId,
        fechaEntrega: fechaEntrega || null,
        ordenCompraCliente: ordenCompra || null,
        notas: notas || null,
        partidas: lineas.map((l) => ({
          id: l.id ?? null,
          productoId: l.producto!.id,
          unidadId: l.unidadId,
          cantidad: Number(l.cantidad),
          tipo: l.tipo ?? 'Venta',
        })),
      };
      const guardado = pedido ? await actualizar.mutateAsync({ id: pedido.id, data }) : await crear.mutateAsync({ data });
      toast.success(pedido ? 'Pedido actualizado.' : `Pedido ${guardado.folio} capturado.`);
      await navegar({ to: '/ventas/pedidos/$id', params: { id: guardado.id } });
    } catch (e) {
      setError(mensajeError(e));
    } finally {
      setEnviando(false);
    }
  };

  return (
    <>
      <EncabezadoPagina
        titulo={pedido ? `Editar ${pedido.folio}` : 'Nuevo pedido'}
        subtitulo="Cantidades en la unidad de venta; precios de la lista del cliente, sin impuestos."
        migas={[
          { etiqueta: 'Ventas' },
          { etiqueta: 'Pedidos', a: '/ventas/pedidos' },
          ...(pedido ? [{ etiqueta: pedido.folio, a: `/ventas/pedidos/${pedido.id}` }] : []),
          { etiqueta: pedido ? 'Editar' : 'Nuevo' },
        ]}
      />
      <div className="flex flex-col gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        {existente && (
          <Aviso tipo="advertencia">
            {existente.cliente} ya tiene el pedido {existente.folio} para esa fecha
            {existente.esRuta ? '; los pedidos de ruta son uno por fecha, edita ese.' : '. Puedes capturar otro si hace falta.'}
          </Aviso>
        )}
        <Tarjeta>
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectorCliente
              valor={clienteId}
              cliente={clienteSel}
              onCambiar={(c) => {
                setClienteId(c?.id ?? '');
                setClienteSel(c);
              }}
              className="sm:col-span-2"
            />
            <Campo etiqueta="Fecha de entrega" type="date" value={fechaEntrega} onChange={(e) => setFechaEntrega(e.target.value)} />
            <Campo etiqueta="Orden de compra del cliente (opcional)" value={ordenCompra} onChange={(e) => setOrdenCompra(e.target.value)} />
            <Campo etiqueta="Notas (opcional)" className="sm:col-span-2" value={notas} onChange={(e) => setNotas(e.target.value)} />
          </div>
          {cliente.data && (
            <p className="m-0 mt-3 text-sm text-muted">
              Lista: {cliente.data.listaPrecio} · {cliente.data.condicionPago === 'Credito' ? `Crédito ${cliente.data.diasCredito} días` : 'Contado'}
            </p>
          )}
        </Tarjeta>
        <Tarjeta titulo="Partidas">
          {!clienteId ? (
            <p className="m-0 text-sm text-muted">Elige el cliente para usar su lista de precios.</p>
          ) : (
            <EditorPartidasVenta lineas={lineas} onCambiar={setLineas} precios={precios} conTipo />
          )}
        </Tarjeta>
        <BarraAccion resumen={<span>Total <strong>{formatoMoneda(calculo.totales.total / 100)}</strong></span>}>
          <Boton variante="texto" onClick={() => navegar({ to: pedido ? `/ventas/pedidos/${pedido.id}` : '/ventas/pedidos' })}>
            Cancelar
          </Boton>
          <Boton cargando={enviando} onClick={guardar}>
            {pedido ? 'Guardar cambios' : 'Capturar pedido'}
          </Boton>
        </BarraAccion>
      </div>
    </>
  );
}

type Accion = 'confirmar' | 'asignar' | 'cancelar' | null;

/** RN-VEN-08: ventas `EntregaPedido` del pedido, con enlace al detalle donde está el mapa del GPS. */
function EntregasPedido({ pedidoId }: { pedidoId: string }) {
  const navegar = useNavigate();
  const { data, isLoading } = useListarVentas({ pedidoId, tamano: 100 });
  const columnas = useMemo<ColumnDef<VentaDto, unknown>[]>(
    () => [
      {
        header: 'Folio',
        accessorKey: 'folio',
        cell: ({ row }) => (
          <Link to="/ventas/ventas/$id" params={{ id: row.original.id }} className="font-medium text-primary">
            {row.original.folio}
          </Link>
        ),
      },
      { header: 'Hora', accessorKey: 'fechaHoraServidor', cell: ({ row }) => formatoFechaHora(row.original.fechaHoraDispositivo ?? row.original.fechaHoraServidor) },
      {
        header: 'Ubicación',
        id: 'ubicacion',
        cell: ({ row }) => {
          const v = row.original;
          if (v.latitud == null) return <span className="text-muted">Sin GPS</span>;
          const distancia = v.distanciaClienteM != null ? `${formatoCantidad(v.distanciaClienteM, 0)} m del cliente` : 'Con GPS';
          return v.requiereRevision ? <EtiquetaEstado color="naranja" etiqueta={`${distancia} · revisar`} /> : distancia;
        },
      },
      { header: 'Total', accessorKey: 'total', cell: ({ row }) => formatoMoneda(row.original.total), meta: { alinear: 'fin' } },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );
  const entregas = data?.items ?? [];
  return (
    <Tarjeta titulo="Entregas" subtitulo="Abre una entrega para ver en el mapa dónde se registró.">
      {isLoading ? null : entregas.length === 0 ? (
        <p className="m-0 text-13 text-muted">Este pedido todavía no tiene entregas.</p>
      ) : (
        <TablaDatos
          columnas={columnas}
          datos={entregas}
          idFila={(v) => v.id}
          ordenable={false}
          alClicFila={(v) => navegar({ to: '/ventas/ventas/$id', params: { id: v.id } })}
        />
      )}
    </Tarjeta>
  );
}

export function PedidoDetalle() {
  const { id } = useParams({ strict: false }) as { id: string };
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const puede = tienePermiso('ventas.pedidos');
  const consulta = useObtenerPedido(id);
  const confirmar = useConfirmarPedido();
  const asignar = useAsignarPedido();
  const cancelar = useCancelarPedido();
  const rutas = useRutas();
  const [accion, setAccion] = useState<Accion>(null);
  const [rutaId, setRutaId] = useState('');
  const [pestana, setPestana] = useState('detalle');
  const p = consulta.data;

  const conSolicitado = !!p?.partidas.some((x) => x.cantidadSolicitada != null);
  const columnas = useMemo<ColumnDef<PedidoPartidaDto, unknown>[]>(
    () => [
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.producto}</span>
            <span className="text-xs text-muted">
              {row.original.sku}
              {row.original.tipo === 'CambioFisico' && ' · Cambio físico'}
              {row.original.notas && ` · ${row.original.notas}`}
            </span>
          </span>
        ),
      },
      ...(conSolicitado
        ? ([
            {
              header: 'Solicitado (app)',
              accessorKey: 'cantidadSolicitada',
              cell: ({ row }) => (row.original.cantidadSolicitada != null ? `${formatoCantidad(row.original.cantidadSolicitada)} ${row.original.unidad}` : '—'),
              meta: { alinear: 'fin' },
            },
          ] as ColumnDef<PedidoPartidaDto, unknown>[])
        : []),
      {
        header: 'Cantidad',
        accessorKey: 'cantidad',
        cell: ({ row }) => {
          const x = row.original;
          const corregida = x.cantidadSolicitada != null && x.cantidadSolicitada !== x.cantidad;
          return <span className={corregida ? 'font-semibold text-[var(--tg-status-yellow-fg)]' : undefined}>{`${formatoCantidad(x.cantidad)} ${x.unidad}`}</span>;
        },
        meta: { alinear: 'fin' },
      },
      { header: 'Entregado (base)', accessorKey: 'cantidadEntregadaBase', cell: ({ row }) => `${formatoCantidad(row.original.cantidadEntregadaBase)} de ${formatoCantidad(row.original.cantidadBase)}`, meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Precio', accessorKey: 'precioUnitario', cell: ({ row }) => formatoMoneda(row.original.precioUnitario), meta: { alinear: 'fin' } },
      { header: 'Importe', accessorKey: 'importe', cell: ({ row }) => formatoMoneda(row.original.importe), meta: { alinear: 'fin' } },
    ],
    [conSolicitado],
  );

  return (
    <Pagina>
      <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
        {p && (
          <>
            <EncabezadoPagina
              titulo={`Pedido ${p.folio}`}
              subtitulo={`${p.cliente} · ${formatoFecha(p.fecha)}`}
              migas={[{ etiqueta: 'Ventas' }, { etiqueta: 'Pedidos', a: '/ventas/pedidos' }, { etiqueta: p.folio }]}
              acciones={
                puede && (
                  <>
                    {p.estado === 'Capturado' && (
                      <>
                        <Boton variante="secundario" icono="edit" onClick={() => navegar({ to: '/ventas/pedidos/$id/editar', params: { id: p.id } })}>
                          Editar
                        </Boton>
                        <Boton icono="task_alt" onClick={() => setAccion('confirmar')}>Confirmar</Boton>
                      </>
                    )}
                    {(p.estado === 'Confirmado' || p.estado === 'Asignado') && (
                      <Boton icono="route" onClick={() => { setRutaId(p.rutaId ?? ''); setAccion('asignar'); }}>
                        {p.estado === 'Asignado' ? 'Cambiar ruta' : 'Asignar a ruta'}
                      </Boton>
                    )}
                    {['Capturado', 'Confirmado', 'Asignado'].includes(p.estado) && (
                      <Boton variante="secundario" icono="cancel" onClick={() => setAccion('cancelar')}>Cancelar pedido</Boton>
                    )}
                  </>
                )
              }
            />
            <Pestanas
              valor={pestana}
              onCambiar={setPestana}
              pestanas={[
                {
                  valor: 'detalle',
                  etiqueta: 'Detalle',
                  contenido: (
                    <div className="flex flex-col gap-4">
                      <Tarjeta>
                        <Datos>
                          <Dato etiqueta="Estado"><EtiquetaEstado estado={p.estado} /></Dato>
                          <Dato etiqueta="Origen">{ETIQUETA_ORIGEN[p.origen]}{p.esRuta ? ' · Pedido de ruta' : ''}</Dato>
                          {p.confirmadoEn && <Dato etiqueta="Confirmado">{formatoFechaHora(p.confirmadoEn)}</Dato>}
                          <Dato etiqueta="Entrega">{formatoFecha(p.fechaEntrega)}</Dato>
                          <Dato etiqueta="Ruta">{rutas.find((r) => r.rutaId === p.rutaId)?.codigo ?? 'Sin asignar'}</Dato>
                          <Dato etiqueta="Carga">
                            {p.envioId ? (
                              <Link to="/logistica/cargas/$id" params={{ id: p.envioId }} className="text-primary">
                                Ver carga
                              </Link>
                            ) : (
                              'Sin carga'
                            )}
                          </Dato>
                          <Dato etiqueta="OC del cliente">{p.ordenCompraCliente || '—'}</Dato>
                          {p.notas && <Dato etiqueta="Notas" className="col-span-2">{p.notas}</Dato>}
                        </Datos>
                      </Tarjeta>
                      <TablaDatos columnas={columnas} datos={p.partidas} idFila={(x) => x.id} ordenable={false} />
                      <Tarjeta>
                        <ResumenTotales totales={{ subtotal: Math.round(p.subtotal * 100), ieps: Math.round(p.ieps * 100), iva: Math.round(p.iva * 100), total: Math.round(p.total * 100) }} />
                      </Tarjeta>
                      <EntregasPedido pedidoId={p.id} />
                    </div>
                  ),
                },
                { valor: 'historial', etiqueta: 'Historial', contenido: <HistorialDocumento tabla="pedidos" documentoId={p.id} /> },
              ]}
            />

            <DialogoConfirmacion
              abierto={accion === 'confirmar'}
              onCambiar={(a) => !a && setAccion(null)}
              titulo="Confirmar pedido"
              mensaje={`El pedido ${p.folio} queda listo para asignarse a una ruta.`}
              alConfirmar={async () => {
                await confirmar.mutateAsync({ id: p.id });
                toast.success('Pedido confirmado.');
              }}
            />
            <DialogoConfirmacion
              abierto={accion === 'asignar'}
              onCambiar={(a) => !a && setAccion(null)}
              titulo="Asignar a ruta"
              mensaje="El pedido baja a la app del vendedor de la ruta para entregarse."
              textoConfirmar="Asignar"
              alConfirmar={async () => {
                if (!rutaId) throw new Error('Elige la ruta.');
                await asignar.mutateAsync({ id: p.id, data: { rutaId } });
                toast.success('Pedido asignado.');
              }}
            >
              <SelectorRuta vacio="Elige la ruta" value={rutaId} onChange={setRutaId} />
            </DialogoConfirmacion>
            <DialogoConfirmacion
              abierto={accion === 'cancelar'}
              onCambiar={(a) => !a && setAccion(null)}
              titulo="Cancelar pedido"
              mensaje={`El pedido ${p.folio} se cancelará y ya no podrá entregarse.`}
              textoConfirmar="Cancelar pedido"
              peligro
              alConfirmar={async () => {
                await cancelar.mutateAsync({ id: p.id });
                toast.success('Pedido cancelado.');
              }}
            />
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
