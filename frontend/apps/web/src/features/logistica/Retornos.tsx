import { useNavigate } from '@tanstack/react-router';
import {
  listarClientes,
  mensajeError,
  useListarCambiosPendientes,
  useListarRetornos,
  useRegistrarRetorno,
  type CondicionRetorno,
  type ProductoDto,
  type RetornoDto,
  type TipoRetorno,
} from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, sumarDias } from '@tg/shared';
import {
  Aviso,
  BarraAccion,
  Boton,
  BotonIcono,
  BuscadorSeleccion,
  Campo,
  Casilla,
  EtiquetaEstado,
  Selector,
  TablaDatos,
  Tarjeta,
  etiquetaEnum,
  toast,
  type ColumnDef,
  type OpcionBusqueda,
} from '@tg/ui';
import { useCallback, useMemo, useState } from 'react';
import { BarraFiltros, SelectorMotivo, SelectorProducto } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { SelectorRuta } from './comunes-logistica';

const TIPOS: { valor: TipoRetorno; etiqueta: string }[] = [
  { valor: 'DevolucionCliente', etiqueta: 'Devolución de cliente' },
  { valor: 'RechazoEntrega', etiqueta: 'Rechazo de entrega' },
  { valor: 'CambioFisico', etiqueta: 'Cambio físico' },
];

function DetalleRetorno({ retorno }: { retorno: RetornoDto }) {
  return (
    <ul className="m-0 flex list-none flex-col gap-1 p-0 text-sm">
      {retorno.partidas.map((p) => (
        <li key={p.id} className="flex flex-wrap gap-x-4">
          <span className="font-medium">
            {p.sku} · {p.producto}
          </span>
          <span>
            {formatoCantidad(p.cantidadBase)} {p.unidad}
          </span>
          <span>Lote {p.lote}</span>
          {p.sentido === 'Entregado' ? (
            <EtiquetaEstado etiqueta={p.cambioPendienteId ? 'Entregado (salda pendiente)' : 'Entregado'} color="azul" />
          ) : (
            <EtiquetaEstado etiqueta={p.condicion === 'Vendible' ? 'Vendible' : 'No vendible'} color={p.condicion === 'Vendible' ? 'verde' : 'naranja'} />
          )}
          {p.motivo && <span className="text-muted">{p.motivo}</span>}
        </li>
      ))}
      {retorno.notas && <li className="text-muted">Notas: {retorno.notas}</li>}
    </ul>
  );
}

export function Retornos() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const hoy = fechaOperativa();
  const [desde, setDesde] = useState(sumarDias(hoy, -7));
  const [hasta, setHasta] = useState(hoy);
  const [rutaId, setRutaId] = useState('');
  const [tipo, setTipo] = useState('');
  const [pagina, setPagina] = useState(1);
  const { data, isLoading, isFetching } = useListarRetornos({
    desde: desde || undefined,
    hasta: hasta || undefined,
    rutaId: rutaId || undefined,
    tipo: (tipo || undefined) as TipoRetorno | undefined,
    pagina,
    tamano: 50,
  });
  const columnas = useMemo<ColumnDef<RetornoDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Ruta', accessorKey: 'rutaCodigo' },
      { header: 'Tipo', accessorKey: 'tipo', cell: ({ row }) => etiquetaEnum(row.original.tipo) },
      { header: 'Cliente', accessorKey: 'cliente', cell: ({ row }) => row.original.cliente ?? '—', meta: { ocultarEnMovil: true } },
      {
        header: 'Origen',
        id: 'origen',
        cell: ({ row }) => (row.original.dispositivoId ? 'App' : 'Web'),
        meta: { ocultarEnMovil: true },
      },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Retornos"
        subtitulo="Devoluciones de clientes y rechazos de entrega. Lo no vendible sale como merma de la camioneta."
        acciones={
          tienePermiso('logistica.retornos') && (
            <Boton icono="add" onClick={() => navegar({ to: '/logistica/retornos/nuevo' })}>
              Registrar retorno
            </Boton>
          )
        }
      />
      <BarraFiltros>
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => (setDesde(e.target.value), setPagina(1))} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => (setHasta(e.target.value), setPagina(1))} />
        <SelectorRuta vacio="Todas" value={rutaId} onChange={(v) => (setRutaId(v), setPagina(1))} />
        <Selector etiqueta="Tipo" vacio="Todos" opciones={TIPOS} value={tipo} onChange={(e) => (setTipo(e.target.value), setPagina(1))} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(r) => r.id}
        detalle={(r) => <DetalleRetorno retorno={r} />}
        mensajeVacio="No hay retornos en ese periodo."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </Pagina>
  );
}

interface PartidaRetorno {
  clave: number;
  producto: ProductoDto | null;
  cantidad: string;
  lote: string;
  condicion: '' | CondicionRetorno;
  motivoMermaId: string;
  entregado: boolean;
  cambioPendienteId: string;
  dejarPendiente: boolean;
}

let clave = 1;
const nueva = (): PartidaRetorno => ({
  clave: clave++,
  producto: null,
  cantidad: '',
  lote: '',
  condicion: '',
  motivoMermaId: '',
  entregado: false,
  cambioPendienteId: '',
  dejarPendiente: true,
});

export function RetornoNuevo() {
  const navegar = useNavigate();
  const registrar = useRegistrarRetorno();
  const [tipo, setTipo] = useState<TipoRetorno>('DevolucionCliente');
  const [rutaId, setRutaId] = useState('');
  const [fecha, setFecha] = useState(fechaOperativa());
  const [cliente, setCliente] = useState<OpcionBusqueda | null>(null);
  const [notas, setNotas] = useState('');
  const [partidas, setPartidas] = useState<PartidaRetorno[]>([nueva()]);
  const [intento, setIntento] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const buscarClientes = useCallback(async (texto: string): Promise<OpcionBusqueda[]> => {
    const r = await listarClientes({ buscar: texto || undefined, activo: true, tamano: 30 });
    return r.items.map((c) => ({ valor: c.id, etiqueta: `${c.codigo} · ${c.nombre}`, detalle: c.canal }));
  }, []);

  const esCambio = tipo === 'CambioFisico';
  const pendientes = useListarCambiosPendientes(
    { clienteId: cliente?.valor, estado: 'Pendiente' },
    { query: { enabled: esCambio && !!cliente } },
  );
  const cambiar = (k: number, c: Partial<PartidaRetorno>) => setPartidas((ps) => ps.map((p) => (p.clave === k ? { ...p, ...c } : p)));
  const validas = partidas.filter((p) => p.producto && Number(p.cantidad) > 0);

  const guardar = async () => {
    setIntento(true);
    setError(null);
    const motivoFaltante = validas.some((p) => !(esCambio && p.entregado) && p.condicion === 'NoVendible' && !p.motivoMermaId);
    if (!rutaId || validas.length === 0 || motivoFaltante || (esCambio && !cliente)) return;
    try {
      const r = await registrar.mutateAsync({
        data: {
          tipo,
          rutaId,
          fechaOperativa: fecha || null,
          clienteId: cliente?.valor ?? null,
          notas: notas || null,
          partidas: validas.map((p) => ({
            productoId: p.producto!.id,
            cantidad: Number(p.cantidad),
            lote: p.lote.trim() || null,
            condicion: p.condicion || null,
            motivoMermaId: p.motivoMermaId || null,
            sentido: esCambio && p.entregado ? 'Entregado' : 'Retirado',
            cambioPendienteId: esCambio && p.entregado ? p.cambioPendienteId || null : null,
            dejarPendiente: p.dejarPendiente,
          })),
        },
      });
      toast.success(`Retorno ${r.folio} registrado.`);
      await navegar({ to: '/logistica/retornos' });
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <Pagina ancho="medio">
      <EncabezadoPagina
        titulo="Registrar retorno"
        subtitulo="Sin condición indicada, la tortilla devuelta se considera no vendible [SUP-13]."
        migas={[{ etiqueta: 'Logística' }, { etiqueta: 'Retornos', a: '/logistica/retornos' }, { etiqueta: 'Nuevo' }]}
      />
      <div className="flex flex-col gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        {tipo === 'RechazoEntrega' && (
          <Aviso tipo="info">El rechazo de entrega no genera movimientos: el producto sigue en la camioneta (RN-LOG-05).</Aviso>
        )}
        {esCambio && (
          <Aviso tipo="info">
            Lo retirado entra a la camioneta (lo no vendible es merma) y lo entregado sale de ella, sin venta. Lo retirado que no se
            entregue queda como cambio pendiente del cliente, salvo que lo indiques (RN-LOG-15, 16).
          </Aviso>
        )}
        <Tarjeta titulo="Datos del retorno">
          <div className="grid gap-4 sm:grid-cols-2">
            <Selector etiqueta="Tipo" opciones={TIPOS} value={tipo} onChange={(e) => setTipo(e.target.value as TipoRetorno)} />
            <SelectorRuta vacio="Elige la ruta" value={rutaId} onChange={setRutaId} error={intento && !rutaId ? 'Elige la ruta.' : undefined} />
            <Campo etiqueta="Fecha operativa" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
            <BuscadorSeleccion
              etiqueta={esCambio ? 'Cliente' : 'Cliente (opcional)'}
              valor={cliente?.valor}
              seleccion={cliente}
              buscar={buscarClientes}
              onCambiar={(_, o) => setCliente(o)}
              error={intento && esCambio && !cliente ? 'Un cambio físico requiere el cliente.' : undefined}
            />
            <Campo etiqueta="Notas (opcional)" className="sm:col-span-2" value={notas} onChange={(e) => setNotas(e.target.value)} />
          </div>
        </Tarjeta>
        <Tarjeta
          titulo="Productos"
          acciones={
            <Boton variante="secundario" tamano="chico" icono="add" onClick={() => setPartidas((ps) => [...ps, nueva()])}>
              Agregar producto
            </Boton>
          }
        >
          <div className="flex flex-col gap-3">
            {partidas.map((p) => (
              <div key={p.clave} className="grid items-start gap-3 rounded-xl border border-row-divider p-3 sm:grid-cols-2">
                <SelectorProducto
                  className="sm:col-span-2"
                  valor={p.producto?.id}
                  producto={p.producto}
                  filtro={{ esVendible: true }}
                  onCambiar={(producto) => cambiar(p.clave, { producto })}
                  error={intento && !p.producto ? 'Elige el producto.' : undefined}
                />
                <Campo
                  etiqueta={`Cantidad${p.producto ? ` (${p.producto.unidadBaseClave})` : ''}`}
                  type="number"
                  step="any"
                  inputMode="decimal"
                  value={p.cantidad}
                  onChange={(e) => cambiar(p.clave, { cantidad: e.target.value })}
                  error={intento && !(Number(p.cantidad) > 0) ? 'Mayor que cero.' : undefined}
                />
                <Campo etiqueta="Lote (opcional)" value={p.lote} onChange={(e) => cambiar(p.clave, { lote: e.target.value })} />
                {esCambio && (
                  <Selector
                    etiqueta="Movimiento"
                    opciones={[
                      { valor: 'Retirado', etiqueta: 'Retirado (entra a la camioneta)' },
                      { valor: 'Entregado', etiqueta: 'Entregado (sale a cambio)' },
                    ]}
                    value={p.entregado ? 'Entregado' : 'Retirado'}
                    onChange={(e) => cambiar(p.clave, { entregado: e.target.value === 'Entregado' })}
                  />
                )}
                {esCambio && p.entregado && (
                  <Selector
                    etiqueta="Salda pendiente"
                    vacio="Ninguno (entrega normal)"
                    opciones={(pendientes.data ?? [])
                      .filter((x) => !p.producto || x.productoId === p.producto.id)
                      .map((x) => ({
                        valor: x.id,
                        etiqueta: `${x.sku}: ${formatoCantidad(x.restanteBase)} ${x.unidad} del ${formatoFecha(x.fechaOperativa)}`,
                      }))}
                    value={p.cambioPendienteId}
                    onChange={(e) => cambiar(p.clave, { cambioPendienteId: e.target.value })}
                  />
                )}
                {esCambio && !p.entregado && (
                  <Casilla
                    etiqueta="Lo no entregado queda pendiente"
                    descripcion="Desmárcalo si el cambio no está autorizado."
                    checked={p.dejarPendiente}
                    onChange={(e) => cambiar(p.clave, { dejarPendiente: e.target.checked })}
                  />
                )}
                {!(esCambio && p.entregado) && (
                <Selector
                  etiqueta="Condición"
                  vacio="Por defecto"
                  opciones={[
                    { valor: 'Vendible', etiqueta: 'Vendible (regresa a la camioneta)' },
                    { valor: 'NoVendible', etiqueta: 'No vendible (merma)' },
                  ]}
                  value={p.condicion}
                  onChange={(e) => cambiar(p.clave, { condicion: e.target.value as PartidaRetorno['condicion'] })}
                />
                )}
                <div className="flex items-start gap-2">
                  {!(esCambio && p.entregado) && (
                    <SelectorMotivo
                      aplica="retorno"
                      className="flex-1"
                      vacio={p.condicion === 'NoVendible' ? 'Elige un motivo' : 'Sin motivo'}
                      value={p.motivoMermaId}
                      onChange={(v) => cambiar(p.clave, { motivoMermaId: v })}
                      error={intento && p.condicion === 'NoVendible' && !p.motivoMermaId ? 'Elige el motivo.' : undefined}
                    />
                  )}
                  <BotonIcono
                    icono="delete"
                    etiqueta="Quitar producto"
                    className="mt-2"
                    disabled={partidas.length === 1}
                    onClick={() => setPartidas((ps) => ps.filter((x) => x.clave !== p.clave))}
                  />
                </div>
              </div>
            ))}
          </div>
        </Tarjeta>
        <BarraAccion resumen={`${validas.length} producto(s)`}>
          <Boton variante="texto" onClick={() => navegar({ to: '/logistica/retornos' })}>
            Cancelar
          </Boton>
          <Boton cargando={registrar.isPending} onClick={guardar}>
            Registrar retorno
          </Boton>
        </BarraAccion>
      </div>
    </Pagina>
  );
}
