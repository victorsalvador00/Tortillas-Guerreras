import { useNavigate, useParams } from '@tanstack/react-router';
import {
  mensajeError,
  useCancelarEnvio,
  useConsultarKardex,
  useCrearEnvio,
  useDespacharEnvio,
  useListarEnvios,
  useObtenerEnvio,
  type EnvioDto,
  type EnvioPartidaDto,
  type EstadoEnvio,
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
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
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
            <Boton icono="add" onClick={() => navegar({ to: '/logistica/cargas/nueva' })}>
              Nueva carga
            </Boton>
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

export function CargaNueva() {
  const navegar = useNavigate();
  const crear = useCrearEnvio();
  const [rutaId, setRutaId] = useState('');
  const [tipo, setTipo] = useState<TipoEnvio>('CargaInicial');
  const [fecha, setFecha] = useState(fechaOperativa());
  const [notas, setNotas] = useState('');
  const [partidas, setPartidas] = useState<PartidaCaptura[]>([partidaVacia()]);
  const [error, setError] = useState<string | null>(null);
  const [intento, setIntento] = useState(false);

  const validas = partidas.filter((p) => p.producto && Number(p.cantidad) > 0);
  const actualizar = (clave: number, cambio: Partial<PartidaCaptura>) =>
    setPartidas((ps) => ps.map((p) => (p.clave === clave ? { ...p, ...cambio } : p)));

  const guardar = async () => {
    setIntento(true);
    setError(null);
    if (!rutaId || validas.length === 0) return;
    try {
      const envio = await crear.mutateAsync({
        data: {
          rutaId,
          tipo,
          fechaOperativa: fecha || null,
          notas: notas || null,
          partidas: validas.map((p) => ({ productoId: p.producto!.id, cantidad: Number(p.cantidad), lote: p.lote.trim() || null })),
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
            <SelectorRuta vacio="Elige la ruta" value={rutaId} onChange={setRutaId} error={intento && !rutaId ? 'Elige la ruta.' : undefined} />
            <Selector etiqueta="Tipo" opciones={TIPOS_ENVIO} value={tipo} onChange={(e) => setTipo(e.target.value as TipoEnvio)} />
            <Campo etiqueta="Fecha operativa" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
            <Campo etiqueta="Notas (opcional)" value={notas} onChange={(e) => setNotas(e.target.value)} />
          </div>
        </Tarjeta>
        <Tarjeta
          titulo="Productos"
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
                  error={intento && !p.producto ? 'Elige el producto.' : undefined}
                />
                <Campo
                  etiqueta={`Cantidad${p.producto ? ` (${p.producto.unidadBaseClave})` : ''}`}
                  type="number"
                  step="any"
                  inputMode="decimal"
                  value={p.cantidad}
                  onChange={(e) => actualizar(p.clave, { cantidad: e.target.value })}
                  error={intento && !(Number(p.cantidad) > 0) ? 'Mayor que cero.' : undefined}
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
                  disabled={partidas.length === 1}
                  onClick={() => setPartidas((ps) => ps.filter((x) => x.clave !== p.clave))}
                />
              </div>
            ))}
          </div>
        </Tarjeta>
        <BarraAccion resumen={`${validas.length} producto(s) en la carga`}>
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
  const { id } = useParams({ strict: false }) as { id: string };
  const { tienePermiso } = useSesion();
  const consulta = useObtenerEnvio(id);
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
                puedeCargar &&
                envio.estado === 'Preparado' && (
                  <>
                    <Boton variante="secundario" icono="block" onClick={() => setAccion('cancelar')}>
                      Cancelar
                    </Boton>
                    <Boton icono="local_shipping" onClick={() => setAccion('despachar')}>
                      Despachar
                    </Boton>
                  </>
                )
              }
            />
            <Tarjeta className="mb-4">
              <Datos>
                <Dato etiqueta="Estado">
                  <EtiquetaEstado estado={envio.estado} />
                </Dato>
                <Dato etiqueta="Origen">{envio.almacenOrigen}</Dato>
                <Dato etiqueta="Destino">{envio.almacenDestino}</Dato>
                <Dato etiqueta="Despachado">{formatoFechaHora(envio.despachadoEn)}</Dato>
                {envio.notas && <Dato etiqueta="Notas">{envio.notas}</Dato>}
              </Datos>
            </Tarjeta>
            <h2 className="mb-2 text-base font-medium">Productos</h2>
            <TablaDatos columnas={columnas} datos={envio.partidas} idFila={(p) => p.id} ordenable={false} />
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
