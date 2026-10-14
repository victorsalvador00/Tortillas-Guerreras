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
  useObtenerPedido,
  type ClienteDto,
  type EstadoPedido,
  type PedidoDto,
  type PedidoPartidaDto,
} from '@tg/api-client';
import { formatoCantidad, formatoFecha, formatoMoneda, sumarDias, fechaOperativa } from '@tg/shared';
import {
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
import { BarraFiltros, exportarCsv } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { EditorPartidasVenta, ResumenTotales } from './EditorPartidasVenta';
import { calcularLineas, nuevaLinea, type LineaVenta } from './partidas';
import { SelectorCliente, SelectorRuta, useRutas } from './selectores';

const ESTADOS: EstadoPedido[] = ['Capturado', 'Confirmado', 'Asignado', 'Entregado', 'EntregadoParcial', 'Cancelado'];

export function Pedidos() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const rutas = useRutas();
  const [estado, setEstado] = useState('');
  const [rutaId, setRutaId] = useState('');
  const [desde, setDesde] = useState(sumarDias(fechaOperativa(), -30));
  const [hasta, setHasta] = useState('');
  const [pagina, setPagina] = useState(1);
  const { data, isLoading, isFetching } = useListarPedidos({
    estado: (estado || undefined) as EstadoPedido | undefined,
    rutaId: rutaId || undefined,
    desde: desde || undefined,
    hasta: hasta || undefined,
    pagina,
    tamano: 50,
  });
  const nombreRuta = (id?: string | null) => rutas.find((r) => r.rutaId === id)?.codigo ?? '—';

  const columnas = useMemo<ColumnDef<PedidoDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Cliente', accessorKey: 'cliente' },
      { header: 'Fecha', accessorKey: 'fecha', cell: ({ row }) => formatoFecha(row.original.fecha), meta: { ocultarEnMovil: true } },
      { header: 'Entrega', accessorKey: 'fechaEntrega', cell: ({ row }) => formatoFecha(row.original.fechaEntrega), meta: { ocultarEnMovil: true } },
      { header: 'Ruta', accessorKey: 'rutaId', cell: ({ row }) => nombreRuta(row.original.rutaId), meta: { ocultarEnMovil: true } },
      { header: 'Total', accessorKey: 'total', cell: ({ row }) => formatoMoneda(row.original.total), meta: { alinear: 'fin' } },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [rutas],
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
      <BarraFiltros>
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={ESTADOS.map((e) => ({ valor: e, etiqueta: e === 'EntregadoParcial' ? 'Entregado parcial' : e }))}
          value={estado}
          onChange={(e) => {
            setEstado(e.target.value);
            setPagina(1);
          }}
        />
        <SelectorRuta vacio="Todas" value={rutaId} onChange={(v) => { setRutaId(v); setPagina(1); }} />
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => { setDesde(e.target.value); setPagina(1); }} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => { setHasta(e.target.value); setPagina(1); }} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(p) => p.id}
        alClicFila={(p) => navegar({ to: '/ventas/pedidos/$id', params: { id: p.id } })}
        mensajeVacio="No hay pedidos con esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </Pagina>
  );
}

/** Convierte las partidas guardadas en líneas editables (necesita los productos para unidades e impuestos). */
async function lineasDePedido(partidas: PedidoPartidaDto[]): Promise<LineaVenta[]> {
  const productos = await Promise.all([...new Set(partidas.map((p) => p.productoId))].map((id) => obtenerProducto(id)));
  return partidas.map((p) => ({
    ...nuevaLinea(),
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
  const calculo = calcularLineas(lineas, lista.data?.partidas);

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
        partidas: lineas.map((l) => ({ productoId: l.producto!.id, unidadId: l.unidadId, cantidad: Number(l.cantidad) })),
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
            <EditorPartidasVenta lineas={lineas} onCambiar={setLineas} precios={lista.data?.partidas} />
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
  const p = consulta.data;

  const columnas = useMemo<ColumnDef<PedidoPartidaDto, unknown>[]>(
    () => [
      { header: 'Producto', accessorKey: 'producto', cell: ({ row }) => <span><span className="block">{row.original.producto}</span><span className="text-xs text-muted">{row.original.sku}</span></span> },
      { header: 'Cantidad', accessorKey: 'cantidad', cell: ({ row }) => `${formatoCantidad(row.original.cantidad)} ${row.original.unidad}`, meta: { alinear: 'fin' } },
      { header: 'Entregado (base)', accessorKey: 'cantidadEntregadaBase', cell: ({ row }) => `${formatoCantidad(row.original.cantidadEntregadaBase)} de ${formatoCantidad(row.original.cantidadBase)}`, meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Precio', accessorKey: 'precioUnitario', cell: ({ row }) => formatoMoneda(row.original.precioUnitario), meta: { alinear: 'fin' } },
      { header: 'Importe', accessorKey: 'importe', cell: ({ row }) => formatoMoneda(row.original.importe), meta: { alinear: 'fin' } },
    ],
    [],
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
            <div className="flex flex-col gap-4">
              <Tarjeta>
                <Datos>
                  <Dato etiqueta="Estado"><EtiquetaEstado estado={p.estado} /></Dato>
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
            </div>

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
