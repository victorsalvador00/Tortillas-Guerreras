import { useNavigate, useParams } from '@tanstack/react-router';
import {
  mensajeError,
  useCancelarEnvio,
  useConsultarKardex,
  useCrearEnvio,
  useDespacharEnvio,
  useListarEnvios,
  useListarPedidos,
  useListarRutas,
  useListarVehiculos,
  useListarVendedores,
  useObtenerEnvio,
  type EnvioDto,
  type EnvioPartidaDto,
  type EstadoEnvio,
  type PedidoDto,
  type ProductoDto,
  type TipoEnvio,
} from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoFechaHora } from '@tg/shared';
import {
  Aviso,
  BarraAccion,
  Boton,
  BotonIcono,
  Campo,
  Casilla,
  Dato,
  Datos,
  DialogoConfirmacion,
  EtiquetaEstado,
  Selector,
  TablaDatos,
  Tarjeta,
  etiquetaEnum,
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros, SelectorProducto, TablaMovimientos } from '@/componentes/comunes';
import { BotonRastrear } from '@/features/inventario/Trazabilidad';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { DialogoCargasSugeridas } from './CargasSugeridas';
import { SelectorRuta } from './comunes-logistica';

const ESTADOS_ENVIO: EstadoEnvio[] = ['Preparado', 'EnRuta', 'Liquidado', 'Cancelado'];
const TIPOS_ENVIO: { valor: TipoEnvio; etiqueta: string }[] = [
  { valor: 'CargaInicial', etiqueta: 'Carga inicial' },
  { valor: 'Recarga', etiqueta: 'Recarga' },
];

export function Cargas() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const [fecha, setFecha] = useState(fechaOperativa());
  const [rutaId, setRutaId] = useState('');
  const [estado, setEstado] = useState('');
  const [pagina, setPagina] = useState(1);
  const [sugeridas, setSugeridas] = useState(false);
  const { data, isLoading, isFetching } = useListarEnvios({
    fecha: fecha || undefined,
    rutaId: rutaId || undefined,
    estado: (estado || undefined) as EstadoEnvio | undefined,
    pagina,
    tamano: 50,
  });
  const columnas = useMemo<ColumnDef<EnvioDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Ruta', accessorKey: 'rutaCodigo' },
      { header: 'Chofer', accessorKey: 'choferNombre', cell: ({ row }) => row.original.choferNombre ?? '—', meta: { ocultarEnMovil: true } },
      { header: 'Tipo', accessorKey: 'tipo', cell: ({ row }) => etiquetaEnum(row.original.tipo) },
      { header: 'Partidas', id: 'partidas', cell: ({ row }) => row.original.partidas.length, meta: { alinear: 'fin', ocultarEnMovil: true } },
      {
        header: 'Despachado',
        accessorKey: 'despachadoEn',
        cell: ({ row }) => formatoFechaHora(row.original.despachadoEn),
        meta: { ocultarEnMovil: true },
      },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Cargas del día"
        subtitulo="Cargas iniciales y recargas de cada ruta. Al despachar pasan del almacén PT a la camioneta."
        acciones={
          tienePermiso('logistica.cargar') && (
            <>
              <Boton variante="secundario" icono="auto_awesome" onClick={() => setSugeridas(true)}>
                Cargas sugeridas
              </Boton>
              <Boton icono="add" onClick={() => navegar({ to: '/logistica/cargas/nueva' })}>
                Nueva carga
              </Boton>
            </>
          )
        }
      />
      <BarraFiltros>
        <Campo etiqueta="Fecha operativa" type="date" value={fecha} onChange={(e) => (setFecha(e.target.value), setPagina(1))} />
        <SelectorRuta vacio="Todas" value={rutaId} onChange={(v) => (setRutaId(v), setPagina(1))} />
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={ESTADOS_ENVIO.map((e) => ({ valor: e, etiqueta: etiquetaEnum(e) }))}
          value={estado}
          onChange={(e) => (setEstado(e.target.value), setPagina(1))}
        />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(e) => e.id}
        alClicFila={(e) => navegar({ to: '/logistica/cargas/$id', params: { id: e.id } })}
        mensajeVacio="No hay cargas para esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
      {sugeridas && <DialogoCargasSugeridas fecha={fecha || fechaOperativa()} onCerrar={() => setSugeridas(false)} />}
    </Pagina>
  );
}

interface PartidaCaptura {
  clave: number;
  producto: ProductoDto | null;
  cantidad: string;
  lote: string;
}

let siguienteClave = 1;
const partidaVacia = (): PartidaCaptura => ({ clave: siguienteClave++, producto: null, cantidad: '', lote: '' });

interface TotalPedidos {
  productoId: string;
  sku: string;
  producto: string;
  cantidadBase: number;
}

/** Suma, por producto y en unidad base, lo que falta entregar de los pedidos elegidos. */
function totalesDePedidos(pedidos: PedidoDto[]): TotalPedidos[] {
  const porProducto = new Map<string, TotalPedidos>();
  for (const pedido of pedidos) {
    for (const p of pedido.partidas) {
      const pendiente = p.cantidadBase - p.cantidadEntregadaBase;
      if (pendiente <= 0) continue;
      const total = porProducto.get(p.productoId);
      if (total) total.cantidadBase += pendiente;
      else porProducto.set(p.productoId, { productoId: p.productoId, sku: p.sku, producto: p.producto, cantidadBase: pendiente });
    }
  }
  return [...porProducto.values()].sort((a, b) => a.sku.localeCompare(b.sku));
}

const columnasTotales: ColumnDef<TotalPedidos, unknown>[] = [
  { header: 'SKU', accessorKey: 'sku' },
  { header: 'Producto', accessorKey: 'producto' },
  {
    header: 'Cantidad (unidad base)',
    accessorKey: 'cantidadBase',
    cell: ({ row }) => formatoCantidad(row.original.cantidadBase),
    meta: { alinear: 'fin' },
  },
];

const columnasPedidosCarga: ColumnDef<PedidoDto, unknown>[] = [
  { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
  { header: 'Cliente', accessorKey: 'cliente' },
  { header: 'Entrega', id: 'entrega', cell: ({ row }) => formatoFecha(row.original.fechaEntrega ?? row.original.fecha) },
  { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
];

export function CargaNueva() {
  const navegar = useNavigate();
  const crear = useCrearEnvio();
  const [rutaId, setRutaId] = useState('');
  const [choferId, setChoferId] = useState('');
  const [vehiculoId, setVehiculoId] = useState('');
  const { data: rutas } = useListarRutas({ activo: true });
  const { data: vendedores } = useListarVendedores();
  const { data: vehiculos } = useListarVehiculos({ activo: true });
  const [tipo, setTipo] = useState<TipoEnvio>('CargaInicial');
  const [fecha, setFecha] = useState(fechaOperativa());
  const [notas, setNotas] = useState('');
  const [partidas, setPartidas] = useState<PartidaCaptura[]>([partidaVacia()]);
  const [error, setError] = useState<string | null>(null);
  const [intento, setIntento] = useState(false);
  const [elegidos, setElegidos] = useState<Set<string>>(new Set());

  // Pedidos asignados a la ruta que aún no van en ninguna carga y se entregan a más tardar ese día (RN-LOG-11).
  const pedidos = useListarPedidos(
    { rutaId, estado: 'Asignado', sinCarga: true, hasta: fecha || undefined, tamano: 100 },
    { query: { enabled: !!rutaId } },
  );
  const disponibles = useMemo(() => (rutaId ? (pedidos.data?.items ?? []) : []), [rutaId, pedidos.data]);
  const pedidosElegidos = useMemo(() => disponibles.filter((p) => elegidos.has(p.id)), [disponibles, elegidos]);
  const totales = useMemo(() => totalesDePedidos(pedidosElegidos), [pedidosElegidos]);
  const conPedidos = pedidosElegidos.length > 0;
  const todos = disponibles.length > 0 && pedidosElegidos.length === disponibles.length;
  const alternar = (id: string, incluir: boolean) =>
    setElegidos((actual) => {
      const nuevo = new Set(actual);
      if (incluir) nuevo.add(id);
      else nuevo.delete(id);
      return nuevo;
    });

  const vacia = (p: PartidaCaptura) => !p.producto && p.cantidad === '';
  const validas = partidas.filter((p) => p.producto && Number(p.cantidad) > 0);
  // Con pedidos elegidos el producto adicional es opcional: se ignoran los renglones vacíos.
  const incompletas = partidas.filter((p) => !(p.producto && Number(p.cantidad) > 0) && !(conPedidos && vacia(p)));
  const exigir = (p: PartidaCaptura) => intento && !(conPedidos && vacia(p));
  const productosEnCarga = new Set([...totales.map((t) => t.productoId), ...validas.map((p) => p.producto!.id)]).size;
  const actualizar = (clave: number, cambio: Partial<PartidaCaptura>) =>
    setPartidas((ps) => ps.map((p) => (p.clave === clave ? { ...p, ...cambio } : p)));

  const guardar = async () => {
    setIntento(true);
    setError(null);
    if (!rutaId || incompletas.length > 0 || (validas.length === 0 && !conPedidos)) return;
    // Producto de los pedidos + producto adicional; el mismo producto sin lote va en un solo renglón.
    const lineas = new Map<string, { productoId: string; cantidad: number; lote: string | null }>();
    const sumar = (productoId: string, cantidad: number, lote: string | null) => {
      const clave = `${productoId}|${lote ?? ''}`;
      const linea = lineas.get(clave);
      if (linea) linea.cantidad += cantidad;
      else lineas.set(clave, { productoId, cantidad, lote });
    };
    totales.forEach((t) => sumar(t.productoId, t.cantidadBase, null));
    validas.forEach((p) => sumar(p.producto!.id, Number(p.cantidad), p.lote.trim() || null));
    try {
      const envio = await crear.mutateAsync({
        data: {
          rutaId,
          choferId: choferId || null,
          vehiculoId: vehiculoId || null,
          tipo,
          fechaOperativa: fecha || null,
          notas: notas || null,
          partidas: [...lineas.values()],
          pedidoIds: pedidosElegidos.map((p) => p.id),
        },
      });
      toast.success(`Carga ${envio.folio} preparada. Despáchala cuando salga la camioneta.`);
      await navegar({ to: '/logistica/cargas/$id', params: { id: envio.id } });
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <Pagina ancho="medio">
      <EncabezadoPagina
        titulo="Nueva carga"
        subtitulo="Cantidades en la unidad base del producto. Sin lote se asigna FEFO al despachar."
        migas={[{ etiqueta: 'Logística' }, { etiqueta: 'Cargas del día', a: '/logistica/cargas' }, { etiqueta: 'Nueva' }]}
      />
      <div className="flex flex-col gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Tarjeta titulo="Datos de la carga">
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectorRuta
              vacio="Elige la ruta"
              value={rutaId}
              onChange={(v) => {
                // RN-LOG-12: se proponen el chofer y el vehículo predeterminados de la ruta.
                const ruta = rutas?.find((r) => r.id === v);
                setRutaId(v);
                setChoferId(ruta?.vendedorId ?? '');
                setVehiculoId(ruta?.vehiculoId ?? '');
                setElegidos(new Set());
              }}
              error={intento && !rutaId ? 'Elige la ruta.' : undefined}
            />
            <Selector
              etiqueta="Chofer"
              vacio="Elige el chofer"
              opciones={(vendedores ?? []).map((u) => ({ valor: u.id, etiqueta: u.nombreCompleto }))}
              value={choferId}
              onChange={(e) => setChoferId(e.target.value)}
              ayuda="Un chofer lleva una sola ruta por día; todas las cargas de la ruta en el día van con él."
            />
            <Selector
              etiqueta="Vehículo"
              vacio="Sin vehículo"
              opciones={(vehiculos ?? []).map((v) => ({ valor: v.id, etiqueta: `${v.placas} · ${v.descripcion}` }))}
              value={vehiculoId}
              onChange={(e) => setVehiculoId(e.target.value)}
            />
            <Selector etiqueta="Tipo" opciones={TIPOS_ENVIO} value={tipo} onChange={(e) => setTipo(e.target.value as TipoEnvio)} />
            <Campo
              etiqueta="Fecha operativa"
              type="date"
              value={fecha}
              onChange={(e) => (setFecha(e.target.value), setElegidos(new Set()))}
            />
            <Campo etiqueta="Notas (opcional)" value={notas} onChange={(e) => setNotas(e.target.value)} />
          </div>
        </Tarjeta>
        {rutaId && (
          <Tarjeta
            titulo="Pedidos asignados a la ruta"
            subtitulo="Pendientes de cargar, con entrega hasta la fecha operativa. Lo que piden se suma a la carga."
            acciones={
              disponibles.length > 1 && (
                <Boton
                  variante="secundario"
                  tamano="chico"
                  icono={todos ? 'remove_done' : 'done_all'}
                  onClick={() => setElegidos(todos ? new Set() : new Set(disponibles.map((p) => p.id)))}
                >
                  {todos ? 'Quitar todos' : 'Incluir todos'}
                </Boton>
              )
            }
          >
            {pedidos.isLoading ? (
              <p className="m-0 text-muted">Buscando pedidos…</p>
            ) : disponibles.length === 0 ? (
              <p className="m-0 text-muted">No hay pedidos asignados a esta ruta pendientes de cargar.</p>
            ) : (
              <div className="flex flex-col divide-y divide-row-divider">
                {disponibles.map((p) => (
                  <Casilla
                    key={p.id}
                    className="py-2.5"
                    checked={elegidos.has(p.id)}
                    onChange={(e) => alternar(p.id, e.target.checked)}
                    etiqueta={
                      <span className="flex flex-wrap gap-x-3">
                        <span className="font-medium">{p.folio}</span>
                        <span>{p.cliente}</span>
                        <span className="text-muted">Entrega {formatoFecha(p.fechaEntrega ?? p.fecha)}</span>
                      </span>
                    }
                    descripcion={p.partidas.map((x) => `${formatoCantidad(x.cantidad)} ${x.unidad} ${x.producto}`).join(' · ')}
                  />
                ))}
              </div>
            )}
            {conPedidos && (
              <div className="mt-4">
                <h3 className="mb-2 text-sm font-medium">Producto para los pedidos elegidos</h3>
                <TablaDatos columnas={columnasTotales} datos={totales} idFila={(t) => t.productoId} ordenable={false} />
              </div>
            )}
          </Tarjeta>
        )}
        <Tarjeta
          titulo={conPedidos ? 'Producto adicional para venta en ruta' : 'Productos'}
          subtitulo={conPedidos ? 'Opcional: lo que la camioneta lleva además de los pedidos.' : undefined}
          acciones={
            <Boton variante="secundario" tamano="chico" icono="add" onClick={() => setPartidas((ps) => [...ps, partidaVacia()])}>
              Agregar producto
            </Boton>
          }
        >
          <div className="flex flex-col gap-3">
            {partidas.map((p) => (
              <div key={p.clave} className="grid items-start gap-3 rounded-xl border border-row-divider p-3 sm:grid-cols-[1fr_189px_189px_auto]">
                <SelectorProducto
                  valor={p.producto?.id}
                  producto={p.producto}
                  filtro={{ esVendible: true }}
                  onCambiar={(producto) => actualizar(p.clave, { producto })}
                  error={exigir(p) && !p.producto ? 'Elige el producto.' : undefined}
                />
                <Campo
                  etiqueta={`Cantidad${p.producto ? ` (${p.producto.unidadBaseClave})` : ''}`}
                  type="number"
                  step="any"
                  inputMode="decimal"
                  value={p.cantidad}
                  onChange={(e) => actualizar(p.clave, { cantidad: e.target.value })}
                  error={exigir(p) && !(Number(p.cantidad) > 0) ? 'Mayor que cero.' : undefined}
                />
                <Campo
                  etiqueta="Lote (opcional)"
                  value={p.lote}
                  disabled={!p.producto?.manejaLote}
                  onChange={(e) => actualizar(p.clave, { lote: e.target.value })}
                />
                <BotonIcono
                  icono="delete"
                  etiqueta="Quitar producto"
                  className="mt-2"
                  disabled={partidas.length === 1 && vacia(p)}
                  onClick={() => setPartidas((ps) => (ps.length === 1 ? [partidaVacia()] : ps.filter((x) => x.clave !== p.clave)))}
                />
              </div>
            ))}
          </div>
        </Tarjeta>
        <BarraAccion
          resumen={
            conPedidos
              ? `${pedidosElegidos.length} pedido(s) · ${productosEnCarga} producto(s) en la carga`
              : `${validas.length} producto(s) en la carga`
          }
        >
          <Boton variante="texto" onClick={() => navegar({ to: '/logistica/cargas' })}>
            Cancelar
          </Boton>
          <Boton cargando={crear.isPending} onClick={guardar}>
            Preparar carga
          </Boton>
        </BarraAccion>
      </div>
    </Pagina>
  );
}

export function CargaDetalle() {
  const navegar = useNavigate();
  const { id } = useParams({ strict: false }) as { id: string };
  const { tienePermiso } = useSesion();
  const consulta = useObtenerEnvio(id);
  const pedidos = useListarPedidos({ envioId: id, tamano: 100 }, { query: { enabled: tienePermiso('ventas.pedidos') } });
  const movimientos = useConsultarKardex({ documentoTipo: 'Envio', documentoId: id, tamano: 200 }, { query: { enabled: tienePermiso('inventario.ver') } });
  const despachar = useDespacharEnvio();
  const cancelar = useCancelarEnvio();
  const [accion, setAccion] = useState<'despachar' | 'cancelar' | null>(null);
  const envio = consulta.data;
  const puedeCargar = tienePermiso('logistica.cargar');

  const columnas = useMemo<ColumnDef<EnvioPartidaDto, unknown>[]>(
    () => [
      { header: 'SKU', accessorKey: 'sku' },
      { header: 'Producto', accessorKey: 'producto' },
      { header: 'Lote', accessorKey: 'lote', cell: ({ row }) => row.original.lote || 'FEFO al despachar' },
      {
        header: 'Cantidad',
        accessorKey: 'cantidadBase',
        cell: ({ row }) => `${formatoCantidad(row.original.cantidadBase)} ${row.original.unidad}`,
        meta: { alinear: 'fin' },
      },
    ],
    [],
  );

  return (
    <Pagina>
      <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
        {envio && (
          <>
            <EncabezadoPagina
              titulo={`Carga ${envio.folio}`}
              subtitulo={`${etiquetaEnum(envio.tipo)} · Ruta ${envio.rutaCodigo} · ${formatoFecha(envio.fechaOperativa)}`}
              migas={[{ etiqueta: 'Logística' }, { etiqueta: 'Cargas del día', a: '/logistica/cargas' }, { etiqueta: envio.folio }]}
              acciones={
                puedeCargar && envio.estado === 'Preparado' ? (
                  <>
                    <Boton variante="secundario" icono="block" onClick={() => setAccion('cancelar')}>
                      Cancelar
                    </Boton>
                    <Boton icono="local_shipping" onClick={() => setAccion('despachar')}>
                      Despachar
                    </Boton>
                  </>
                ) : (
                  envio.estado !== 'Preparado' && envio.estado !== 'Cancelado' && <BotonRastrear tipo="Envio" id={envio.id} etiqueta={envio.folio} />
                )
              }
            />
            <Tarjeta className="mb-4">
              <Datos>
                <Dato etiqueta="Estado">
                  <EtiquetaEstado estado={envio.estado} />
                </Dato>
                <Dato etiqueta="Chofer">{envio.choferNombre ?? '—'}</Dato>
                <Dato etiqueta="Vehículo">{envio.vehiculoPlacas ?? '—'}</Dato>
                <Dato etiqueta="Origen">{envio.almacenOrigen}</Dato>
                <Dato etiqueta="Destino">{envio.almacenDestino}</Dato>
                <Dato etiqueta="Despachado">{formatoFechaHora(envio.despachadoEn)}</Dato>
                {envio.notas && <Dato etiqueta="Notas">{envio.notas}</Dato>}
              </Datos>
            </Tarjeta>
            <h2 className="mb-2 text-base font-medium">Productos</h2>
            <TablaDatos columnas={columnas} datos={envio.partidas} idFila={(p) => p.id} ordenable={false} />
            {(pedidos.data?.items.length ?? 0) > 0 && (
              <>
                <h2 className="mt-6 mb-2 text-base font-medium">Pedidos que lleva</h2>
                <TablaDatos
                  columnas={columnasPedidosCarga}
                  datos={pedidos.data?.items}
                  idFila={(p) => p.id}
                  ordenable={false}
                  alClicFila={(p) => navegar({ to: '/ventas/pedidos/$id', params: { id: p.id } })}
                />
              </>
            )}
            {envio.estado !== 'Preparado' && envio.estado !== 'Cancelado' && tienePermiso('inventario.ver') && (
              <>
                <h2 className="mt-6 mb-2 text-base font-medium">Movimientos de inventario</h2>
                <TablaMovimientos movimientos={movimientos.data?.items} cargando={movimientos.isLoading} conCosto={false} />
              </>
            )}
            <DialogoConfirmacion
              abierto={accion === 'despachar'}
              onCambiar={(a) => !a && setAccion(null)}
              titulo={`Despachar ${envio.folio}`}
              mensaje={`Se registra la salida de PT y la entrada a la camioneta de ${envio.rutaCodigo}; la carga baja a la app. Si es la primera del día se abre la liquidación de la ruta.`}
              textoConfirmar="Despachar"
              alConfirmar={async () => {
                const r = await despachar.mutateAsync({ id });
                toast.success(`Carga ${r.folio} en ruta.`);
              }}
            />
            <DialogoConfirmacion
              abierto={accion === 'cancelar'}
              onCambiar={(a) => !a && setAccion(null)}
              titulo={`Cancelar ${envio.folio}`}
              mensaje="La carga no se despachará. No genera movimientos de inventario."
              textoConfirmar="Cancelar carga"
              peligro
              alConfirmar={async () => {
                await cancelar.mutateAsync({ id });
                toast.success('Carga cancelada.');
              }}
            />
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
