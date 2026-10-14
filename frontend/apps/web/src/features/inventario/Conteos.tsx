import { useNavigate, useParams } from '@tanstack/react-router';
import {
  esErrorApi,
  mensajeError,
  useAbrirConteo,
  useCancelarConteo,
  useCapturarConteo,
  useCerrarConteo,
  useListarConteos,
  useObtenerConteo,
  type ConteoDto,
  type ConteoPartidaDto,
  type EstadoConteo,
  type ProductoDto,
} from '@tg/api-client';
import { formatoCantidad, formatoFecha, formatoFechaHora } from '@tg/shared';
import {
  AreaTexto,
  Aviso,
  Boton,
  Campo,
  Dato,
  Datos,
  Dialogo,
  DialogoConfirmacion,
  EtiquetaEstado,
  Selector,
  TablaDatos,
  Tarjeta,
  cn,
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useMemo, useRef, useState } from 'react';
import { BarraFiltros, SelectorAlmacen, SelectorProducto } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { resumenCierre } from './reglas';

function Diferencia({ valor }: { valor: number }) {
  return (
    <span
      className={cn(
        'font-medium tabular-nums',
        valor > 0 && 'text-[var(--tg-status-green-fg)]',
        valor < 0 && 'text-[var(--tg-status-red-fg)]',
        valor === 0 && 'text-muted',
      )}
    >
      {valor > 0 ? '+' : ''}
      {formatoCantidad(valor)}
    </span>
  );
}

export function Conteos() {
  const navegar = useNavigate();
  const [estado, setEstado] = useState('');
  const { data, isLoading, isFetching } = useListarConteos({ estado: (estado || undefined) as EstadoConteo | undefined });
  const abrir = useAbrirConteo();
  const [dialogo, setDialogo] = useState(false);
  const [almacenId, setAlmacenId] = useState('');
  const [notas, setNotas] = useState('');
  const [error, setError] = useState<string | null>(null);

  const columnas = useMemo<ColumnDef<ConteoDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Almacén', accessorKey: 'almacen' },
      { header: 'Partidas', id: 'partidas', cell: ({ row }) => row.original.partidas.length, meta: { alinear: 'fin' } },
      {
        header: 'Con diferencia',
        id: 'diferencias',
        cell: ({ row }) => resumenCierre(row.original.partidas).partidas,
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );

  const abrirConteo = async () => {
    setError(null);
    if (!almacenId) {
      setError('Elige el almacén a contar.');
      return;
    }
    try {
      const c = await abrir.mutateAsync({ data: { almacenId, notas: notas.trim() || null } });
      toast.success(`Conteo ${c.folio} abierto.`);
      setDialogo(false);
      await navegar({ to: '/inventario/conteos/$id', params: { id: c.id } });
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Conteos físicos"
        subtitulo="Mientras el conteo está abierto el almacén sigue operando; el sistema se toma al capturar cada partida (RN-INV-10)."
        acciones={
          <Boton icono="add" onClick={() => setDialogo(true)}>
            Abrir conteo
          </Boton>
        }
      />
      <BarraFiltros>
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={[
            { valor: 'Abierto', etiqueta: 'Abiertos' },
            { valor: 'Cerrado', etiqueta: 'Cerrados' },
            { valor: 'Cancelado', etiqueta: 'Cancelados' },
          ]}
          value={estado}
          onChange={(e) => setEstado(e.target.value)}
        />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(c) => c.id}
        alClicFila={(c) => navegar({ to: '/inventario/conteos/$id', params: { id: c.id } })}
        mensajeVacio="No hay conteos."
      />
      <Dialogo
        abierto={dialogo}
        onCambiar={setDialogo}
        titulo="Abrir conteo físico"
        acciones={
          <>
            <Boton variante="texto" onClick={() => setDialogo(false)}>
              Cancelar
            </Boton>
            <Boton cargando={abrir.isPending} onClick={abrirConteo}>
              Abrir conteo
            </Boton>
          </>
        }
      >
        <div className="grid gap-4">
          {error && <Aviso tipo="error">{error}</Aviso>}
          <SelectorAlmacen incluirRutas vacio="Elige el almacén" value={almacenId} onChange={setAlmacenId} />
          <AreaTexto etiqueta="Notas (opcional)" value={notas} onChange={(e) => setNotas(e.target.value)} />
        </div>
      </Dialogo>
    </Pagina>
  );
}

/** Captura de conteo pensada para tableta: producto, lote y cantidad grande; luego la tabla de lo capturado. */
export function DetalleConteo() {
  const { id } = useParams({ strict: false }) as { id: string };
  const consulta = useObtenerConteo(id);
  const capturar = useCapturarConteo();
  const cerrar = useCerrarConteo();
  const cancelar = useCancelarConteo();
  const [producto, setProducto] = useState<ProductoDto | null>(null);
  const [lote, setLote] = useState('');
  const [cantidad, setCantidad] = useState('');
  const [errores, setErrores] = useState<Record<string, string>>({});
  const [confirmar, setConfirmar] = useState<'cerrar' | 'cancelar' | null>(null);
  const cantidadRef = useRef<HTMLInputElement>(null);
  const conteo = consulta.data;
  const abierto = conteo?.estado === 'Abierto';

  const columnas = useMemo<ColumnDef<ConteoPartidaDto, unknown>[]>(
    () => [
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block font-medium">{row.original.producto}</span>
            <span className="text-xs text-muted">
              {row.original.sku}
              {row.original.lote ? ` · Lote ${row.original.lote}` : ''}
            </span>
          </span>
        ),
      },
      { header: 'Sistema', accessorKey: 'cantidadSistema', cell: ({ row }) => formatoCantidad(row.original.cantidadSistema), meta: { alinear: 'fin' } },
      {
        header: 'Contado',
        accessorKey: 'cantidadContada',
        cell: ({ row }) => `${formatoCantidad(row.original.cantidadContada)} ${row.original.unidad}`,
        meta: { alinear: 'fin' },
      },
      { header: 'Diferencia', accessorKey: 'diferencia', cell: ({ row }) => <Diferencia valor={row.original.diferencia} />, meta: { alinear: 'fin' } },
      { header: 'Capturado', accessorKey: 'capturadoEn', cell: ({ row }) => formatoFechaHora(row.original.capturadoEn), meta: { ocultarEnMovil: true } },
    ],
    [],
  );

  const registrar = async () => {
    const nuevos: Record<string, string> = {};
    if (!producto) nuevos.producto = 'Elige el producto.';
    if (producto?.manejaLote && !lote.trim()) nuevos.lote = 'Indica el lote contado.';
    const n = Number(cantidad);
    if (cantidad.trim() === '' || !Number.isFinite(n) || n < 0) nuevos.cantidad = 'Captura la cantidad contada.';
    setErrores(nuevos);
    if (Object.keys(nuevos).length > 0 || !producto) return;
    try {
      await capturar.mutateAsync({ id, data: { productoId: producto.id, lote: lote.trim() || null, cantidadContada: n } });
      toast.success(`${producto.nombre}: ${formatoCantidad(n)} ${producto.unidadBaseClave} capturado.`);
      setProducto(null);
      setLote('');
      setCantidad('');
    } catch (e) {
      const campo = esErrorApi(e) ? (e.errorDe('cantidadContada') ?? e.errorDe('lote')) : undefined;
      setErrores({ general: campo ?? mensajeError(e) });
    }
  };

  const resumen = resumenCierre(conteo?.partidas ?? []);

  return (
    <Pagina>
      <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
        {conteo && (
          <>
            <EncabezadoPagina
              titulo={`Conteo ${conteo.folio}`}
              subtitulo={`${conteo.almacen} · ${formatoFecha(conteo.fechaOperativa)}`}
              migas={[{ etiqueta: 'Inventario' }, { etiqueta: 'Conteos físicos', a: '/inventario/conteos' }, { etiqueta: conteo.folio }]}
              acciones={
                abierto && (
                  <>
                    <Boton variante="secundario" icono="close" onClick={() => setConfirmar('cancelar')}>
                      Cancelar conteo
                    </Boton>
                    <Boton icono="task_alt" onClick={() => setConfirmar('cerrar')} disabled={conteo.partidas.length === 0}>
                      Cerrar conteo
                    </Boton>
                  </>
                )
              }
            />
            {!abierto && (
              <Tarjeta className="mb-4">
                <Datos>
                  <Dato etiqueta="Estado">
                    <EtiquetaEstado estado={conteo.estado} />
                  </Dato>
                  <Dato etiqueta="Cerrado">{conteo.cerradoEn ? formatoFechaHora(conteo.cerradoEn) : '—'}</Dato>
                  <Dato etiqueta="Partidas">{conteo.partidas.length}</Dato>
                  <Dato etiqueta="Notas">{conteo.notas}</Dato>
                </Datos>
              </Tarjeta>
            )}
            {abierto && (
              <Tarjeta titulo="Capturar" subtitulo="Volver a capturar un producto y lote reemplaza su cantidad contada." className="mb-4">
                <form
                  className="grid items-start gap-4 md:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)_auto]"
                  onSubmit={(e) => {
                    e.preventDefault();
                    void registrar();
                  }}
                >
                  {errores.general && (
                    <Aviso tipo="error" className="md:col-span-4">
                      {errores.general}
                    </Aviso>
                  )}
                  <SelectorProducto
                    valor={producto?.id}
                    producto={producto}
                    onCambiar={(p) => {
                      setProducto(p);
                      setLote('');
                      setTimeout(() => cantidadRef.current?.focus(), 50);
                    }}
                    error={errores.producto}
                  />
                  <Campo
                    etiqueta={producto?.manejaLote ? 'Lote' : 'Sin lote'}
                    disabled={!producto?.manejaLote}
                    value={lote}
                    onChange={(e) => setLote(e.target.value.toUpperCase())}
                    error={errores.lote}
                  />
                  <Campo
                    ref={cantidadRef}
                    etiqueta="Cantidad contada"
                    type="number"
                    inputMode="decimal"
                    step="any"
                    min="0"
                    className="[&_input]:text-2xl"
                    sufijo={producto?.unidadBaseClave}
                    value={cantidad}
                    onChange={(e) => setCantidad(e.target.value)}
                    error={errores.cantidad}
                  />
                  <Boton type="submit" tamano="grande" icono="add_task" cargando={capturar.isPending}>
                    Registrar
                  </Boton>
                </form>
              </Tarjeta>
            )}
            <TablaDatos
              columnas={columnas}
              datos={conteo.partidas}
              idFila={(p) => p.id}
              mensajeVacio="Aún no se captura ningún producto."
            />
            <DialogoConfirmacion
              abierto={confirmar === 'cerrar'}
              onCambiar={(a) => !a && setConfirmar(null)}
              titulo="Cerrar conteo"
              mensaje={
                resumen.partidas === 0
                  ? 'Ninguna partida tiene diferencia: se cerrará sin ajustes.'
                  : `Se generarán ${resumen.partidas} ajuste(s) en ${conteo.almacen}: ${resumen.entradas} de entrada y ${resumen.salidas} de salida, por la diferencia entre lo contado y lo que había al capturar. El conteo ya no se podrá modificar.`
              }
              textoConfirmar="Cerrar y ajustar"
              alConfirmar={async () => {
                await cerrar.mutateAsync({ id });
                toast.success('Conteo cerrado.');
              }}
            />
            <DialogoConfirmacion
              abierto={confirmar === 'cancelar'}
              onCambiar={(a) => !a && setConfirmar(null)}
              titulo="Cancelar conteo"
              mensaje="Se descarta lo capturado y no se genera ningún ajuste."
              textoConfirmar="Cancelar conteo"
              peligro
              alConfirmar={async () => {
                await cancelar.mutateAsync({ id });
                toast.success('Conteo cancelado.');
              }}
            />
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
