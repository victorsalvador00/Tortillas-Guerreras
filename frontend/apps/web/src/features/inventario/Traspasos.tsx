import { useNavigate, useParams } from '@tanstack/react-router';
import {
  mensajeError,
  useCancelarTraspaso,
  useConsultarKardex,
  useListarTraspasos,
  useObtenerTraspaso,
  useRegistrarTraspaso,
  type TraspasoDto,
  type TraspasoPartidaDto,
} from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoFechaHora, sumarDias } from '@tg/shared';
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
  TablaDatos,
  Tarjeta,
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros, SelectorAlmacen, TablaMovimientos } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { EditorPartidas } from './EditorPartidas';
import { aPartidasTraspaso, nuevaPartida, validarPartidas, type PartidaCaptura } from './partidas';

export function Traspasos() {
  const navegar = useNavigate();
  const hoy = fechaOperativa();
  const [desde, setDesde] = useState(sumarDias(hoy, -30));
  const [hasta, setHasta] = useState(hoy);
  const { data, isLoading, isFetching } = useListarTraspasos({ desde: desde || undefined, hasta: hasta || undefined });
  const columnas = useMemo<ColumnDef<TraspasoDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Origen', accessorKey: 'almacenOrigen' },
      { header: 'Destino', accessorKey: 'almacenDestino' },
      { header: 'Partidas', id: 'partidas', cell: ({ row }) => row.original.partidas.length, meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Traspasos"
        subtitulo="Movimientos entre almacenes que no son de ruta, con el mismo costo en ambos lados (RN-INV-06)."
        acciones={
          <Boton icono="add" onClick={() => navegar({ to: '/inventario/traspasos/nuevo' })}>
            Nuevo traspaso
          </Boton>
        }
      />
      <BarraFiltros>
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(t) => t.id}
        alClicFila={(t) => navegar({ to: '/inventario/traspasos/$id', params: { id: t.id } })}
        mensajeVacio="No hay traspasos en el periodo."
      />
    </Pagina>
  );
}

export function NuevoTraspaso() {
  const navegar = useNavigate();
  const registrar = useRegistrarTraspaso();
  const [origen, setOrigen] = useState('');
  const [destino, setDestino] = useState('');
  const [notas, setNotas] = useState('');
  const [partidas, setPartidas] = useState<PartidaCaptura[]>(() => [nuevaPartida()]);
  const [errores, setErrores] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);

  const guardar = async () => {
    const nuevos = validarPartidas(partidas);
    if (!origen) nuevos.origen = 'Elige el almacén de origen.';
    if (!destino) nuevos.destino = 'Elige el almacén destino.';
    if (origen && origen === destino) nuevos.destino = 'El destino debe ser distinto del origen.';
    setErrores(nuevos);
    setError(null);
    if (Object.keys(nuevos).length > 0) return;
    try {
      const t = await registrar.mutateAsync({
        data: { almacenOrigenId: origen, almacenDestinoId: destino, notas: notas.trim() || null, partidas: aPartidasTraspaso(partidas) },
      });
      toast.success(`Traspaso ${t.folio} registrado.`);
      await navegar({ to: '/inventario/traspasos/$id', params: { id: t.id } });
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Nuevo traspaso"
        subtitulo="Sin lote, la salida del origen se toma por FEFO y el destino recibe los mismos lotes."
        migas={[{ etiqueta: 'Inventario' }, { etiqueta: 'Traspasos', a: '/inventario/traspasos' }, { etiqueta: 'Nuevo' }]}
      />
      <div className="flex flex-col gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Tarjeta>
          <div className="grid gap-4 md:grid-cols-2">
            <SelectorAlmacen etiqueta="Almacén de origen" vacio="Elige el origen" value={origen} onChange={setOrigen} error={errores.origen} />
            <SelectorAlmacen etiqueta="Almacén destino" vacio="Elige el destino" value={destino} onChange={setDestino} error={errores.destino} />
            <AreaTexto etiqueta="Notas (opcional)" className="md:col-span-2" value={notas} onChange={(e) => setNotas(e.target.value)} />
          </div>
        </Tarjeta>
        <Tarjeta titulo="Partidas">
          <EditorPartidas partidas={partidas} onCambiar={setPartidas} errores={errores} />
        </Tarjeta>
        <BarraAccion resumen={`${partidas.length} partida(s)`}>
          <Boton variante="texto" onClick={() => navegar({ to: '/inventario/traspasos' })}>
            Cancelar
          </Boton>
          <Boton cargando={registrar.isPending} onClick={guardar}>
            Registrar traspaso
          </Boton>
        </BarraAccion>
      </div>
    </Pagina>
  );
}

export function DetalleTraspaso() {
  const { id } = useParams({ strict: false }) as { id: string };
  const consulta = useObtenerTraspaso(id);
  const movimientos = useConsultarKardex({ documentoTipo: 'Traspaso', documentoId: id, tamano: 200 });
  const cancelar = useCancelarTraspaso();
  const [confirmar, setConfirmar] = useState(false);
  const t = consulta.data;
  const columnas = useMemo<ColumnDef<TraspasoPartidaDto, unknown>[]>(
    () => [
      { header: 'SKU', accessorKey: 'sku' },
      { header: 'Producto', accessorKey: 'producto' },
      { header: 'Lote', accessorKey: 'lote', cell: ({ row }) => row.original.lote || '—' },
      { header: 'Cantidad', accessorKey: 'cantidad', cell: ({ row }) => `${formatoCantidad(row.original.cantidad)} ${row.original.unidad}`, meta: { alinear: 'fin' } },
    ],
    [],
  );
  return (
    <Pagina>
      <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
        {t && (
          <>
            <EncabezadoPagina
              titulo={`Traspaso ${t.folio}`}
              subtitulo={`${t.almacenOrigen} → ${t.almacenDestino}`}
              migas={[{ etiqueta: 'Inventario' }, { etiqueta: 'Traspasos', a: '/inventario/traspasos' }, { etiqueta: t.folio }]}
              acciones={
                t.estado === 'Registrado' && (
                  <Boton variante="secundario" icono="undo" onClick={() => setConfirmar(true)}>
                    Cancelar traspaso
                  </Boton>
                )
              }
            />
            <Tarjeta className="mb-4">
              <Datos>
                <Dato etiqueta="Estado">
                  <EtiquetaEstado estado={t.estado} />
                </Dato>
                <Dato etiqueta="Fecha operativa">{formatoFecha(t.fechaOperativa)}</Dato>
                <Dato etiqueta="Registrado">{formatoFechaHora(t.creadoEn)}</Dato>
                <Dato etiqueta="Notas">{t.notas}</Dato>
              </Datos>
            </Tarjeta>
            <h2 className="mb-2 text-base font-medium">Partidas</h2>
            <TablaDatos columnas={columnas} datos={t.partidas} idFila={(p) => `${p.productoId}-${p.lote}`} />
            <h2 className="mt-6 mb-2 text-base font-medium">Movimientos generados</h2>
            <TablaMovimientos movimientos={movimientos.data?.items} cargando={movimientos.isLoading} />
            <DialogoConfirmacion
              abierto={confirmar}
              onCambiar={setConfirmar}
              titulo="Cancelar traspaso"
              mensaje={`Se registrará el movimiento contrario: sale de ${t.almacenDestino} y regresa a ${t.almacenOrigen}. Si el destino ya no tiene la existencia, no se podrá cancelar.`}
              textoConfirmar="Cancelar traspaso"
              peligro
              alConfirmar={async () => {
                await cancelar.mutateAsync({ id: t.id });
                toast.success('Traspaso cancelado.');
              }}
            />
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
