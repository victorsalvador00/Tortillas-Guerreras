import { useNavigate, useParams } from '@tanstack/react-router';
import {
  esErrorApi,
  mensajeError,
  useConsultarKardex,
  useListarAjustes,
  useListarMotivosMerma,
  useRegistrarAjuste,
  type AjusteResumenDto,
  type ResultadoDocumentoDto,
} from '@tg/api-client';
import { fechaOperativa, formatoFechaHora, formatoMoneda, sumarDias } from '@tg/shared';
import { AreaTexto, Aviso, BarraAccion, Boton, Campo, Casilla, Datos, Dato, TablaDatos, Tarjeta, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros, SelectorAlmacen, SelectorMotivo, TablaMovimientos } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { EditorPartidas } from './EditorPartidas';
import { aPartidasAjuste, nuevaPartida, validarPartidas, type PartidaCaptura } from './partidas';

export function Ajustes() {
  const navegar = useNavigate();
  const hoy = fechaOperativa();
  const [desde, setDesde] = useState(sumarDias(hoy, -30));
  const [hasta, setHasta] = useState(hoy);
  const [almacenId, setAlmacenId] = useState('');
  const { data, isLoading, isFetching } = useListarAjustes({ desde: desde || undefined, hasta: hasta || undefined, almacenId: almacenId || undefined });
  const { data: motivos } = useListarMotivosMerma();
  const nombreMotivo = useMemo(() => new Map((motivos ?? []).map((m) => [m.id, m.nombre])), [motivos]);

  const columnas = useMemo<ColumnDef<AjusteResumenDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaHora', cell: ({ row }) => formatoFechaHora(row.original.fechaHora) },
      { header: 'Almacén', accessorKey: 'almacenCodigo' },
      {
        header: 'Motivo',
        accessorKey: 'motivoMermaId',
        cell: ({ row }) => (row.original.motivoMermaId ? (nombreMotivo.get(row.original.motivoMermaId) ?? '—') : '—'),
      },
      { header: 'Nota', accessorKey: 'notas', cell: ({ row }) => row.original.notas ?? '—', meta: { ocultarEnMovil: true } },
      { header: 'Partidas', accessorKey: 'partidas', meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Costo', accessorKey: 'costoTotal', cell: ({ row }) => formatoMoneda(row.original.costoTotal), meta: { alinear: 'fin' } },
    ],
    [nombreMotivo],
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Ajustes de inventario"
        subtitulo="Entradas y salidas manuales con motivo y nota (RN-INV-08); se reportan en el tablero."
        acciones={
          <Boton icono="add" onClick={() => navegar({ to: '/inventario/ajustes/nuevo' })}>
            Nuevo ajuste
          </Boton>
        }
      />
      <BarraFiltros>
        <SelectorAlmacen vacio="Todos los almacenes" value={almacenId} onChange={setAlmacenId} />
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(a) => a.documentoId}
        alClicFila={(a) => navegar({ to: '/inventario/ajustes/$id', params: { id: a.documentoId } })}
        mensajeVacio="No hay ajustes en el periodo."
      />
    </Pagina>
  );
}

export function NuevoAjuste() {
  const navegar = useNavigate();
  const registrar = useRegistrarAjuste();
  const [almacenId, setAlmacenId] = useState('');
  const [motivoId, setMotivoId] = useState('');
  const [notas, setNotas] = useState('');
  const [esMerma, setEsMerma] = useState(false);
  const [partidas, setPartidas] = useState<PartidaCaptura[]>(() => [nuevaPartida()]);
  const [errores, setErrores] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [resultado, setResultado] = useState<ResultadoDocumentoDto | null>(null);

  const registrarAjuste = async () => {
    const nuevos = validarPartidas(partidas, { exigirLoteEnEntradas: true, exigirCostoEnEntradas: true });
    if (!almacenId) nuevos.almacen = 'Elige el almacén.';
    if (!motivoId) nuevos.motivo = 'Elige el motivo del ajuste.';
    if (!notas.trim()) nuevos.notas = 'Escribe una nota que explique el ajuste.';
    setErrores(nuevos);
    setError(null);
    if (Object.keys(nuevos).length > 0) return;
    try {
      const r = await registrar.mutateAsync({
        data: { almacenId, motivoMermaId: motivoId, notas: notas.trim(), esMerma, partidas: aPartidasAjuste(partidas) },
      });
      toast.success(`Ajuste ${r.folio} registrado.`);
      setResultado(r);
    } catch (e) {
      setError(mensajeError(e));
      if (esErrorApi(e) && e.errores.notas) setErrores((x) => ({ ...x, notas: e.errores.notas[0] }));
    }
  };

  if (resultado) {
    return (
      <Pagina>
        <EncabezadoPagina
          titulo={`Ajuste ${resultado.folio}`}
          subtitulo="Movimientos generados en el kardex."
          migas={[{ etiqueta: 'Inventario' }, { etiqueta: 'Ajustes', a: '/inventario/ajustes' }, { etiqueta: resultado.folio }]}
          acciones={
            <>
              <Boton variante="secundario" onClick={() => navegar({ to: '/inventario/ajustes' })}>
                Ver ajustes
              </Boton>
              <Boton
                icono="add"
                onClick={() => {
                  setResultado(null);
                  setPartidas([nuevaPartida()]);
                  setNotas('');
                }}
              >
                Otro ajuste
              </Boton>
            </>
          }
        />
        <Aviso tipo="exito" className="mb-4">
          El ajuste quedó registrado con {resultado.movimientos.length} movimiento(s).
        </Aviso>
        <TablaMovimientos movimientos={resultado.movimientos} />
      </Pagina>
    );
  }

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Nuevo ajuste"
        subtitulo="Mermas, caducados, dañados y correcciones de existencia. Las salidas sin lote se toman por FEFO."
        migas={[{ etiqueta: 'Inventario' }, { etiqueta: 'Ajustes', a: '/inventario/ajustes' }, { etiqueta: 'Nuevo' }]}
      />
      <div className="flex flex-col gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Tarjeta>
          <div className="grid gap-4 md:grid-cols-3">
            <SelectorAlmacen vacio="Elige el almacén" value={almacenId} onChange={setAlmacenId} error={errores.almacen} />
            <SelectorMotivo aplica="almacen" value={motivoId} onChange={setMotivoId} error={errores.motivo} />
            <Casilla
              etiqueta="Registrar las salidas como merma"
              descripcion="Si no, se registran como ajuste de salida."
              className="pt-3"
              checked={esMerma}
              onChange={(e) => setEsMerma(e.target.checked)}
            />
            <AreaTexto
              etiqueta="Nota (obligatoria)"
              className="md:col-span-3"
              value={notas}
              onChange={(e) => setNotas(e.target.value)}
              error={errores.notas}
            />
          </div>
        </Tarjeta>
        <Tarjeta titulo="Partidas" subtitulo="Las entradas con costo recalculan el costo promedio del producto.">
          <EditorPartidas partidas={partidas} onCambiar={setPartidas} errores={errores} conSentido />
        </Tarjeta>
        <BarraAccion resumen={`${partidas.length} partida(s)`}>
          <Boton variante="texto" onClick={() => navegar({ to: '/inventario/ajustes' })}>
            Cancelar
          </Boton>
          <Boton cargando={registrar.isPending} onClick={registrarAjuste}>
            Registrar ajuste
          </Boton>
        </BarraAccion>
      </div>
    </Pagina>
  );
}

export function DetalleAjuste() {
  const { id } = useParams({ strict: false }) as { id: string };
  const { data, isLoading } = useConsultarKardex({ documentoTipo: 'Ajuste', documentoId: id, tamano: 200 });
  const primero = data?.items[0];
  return (
    <Pagina>
      <EncabezadoPagina
        titulo={primero ? `Ajuste ${primero.documentoFolio}` : 'Ajuste'}
        migas={[{ etiqueta: 'Inventario' }, { etiqueta: 'Ajustes', a: '/inventario/ajustes' }, { etiqueta: primero?.documentoFolio ?? '…' }]}
      />
      {primero && (
        <Tarjeta className="mb-4">
          <Datos>
            <Dato etiqueta="Fecha">{formatoFechaHora(primero.fechaHora)}</Dato>
            <Dato etiqueta="Almacén">{primero.almacenCodigo}</Dato>
            <Dato etiqueta="Nota">{primero.notas}</Dato>
            <Dato etiqueta="Costo total">{formatoMoneda((data?.items ?? []).reduce((s, m) => s + m.costoTotal, 0))}</Dato>
          </Datos>
        </Tarjeta>
      )}
      <TablaMovimientos movimientos={data?.items} cargando={isLoading} />
    </Pagina>
  );
}
