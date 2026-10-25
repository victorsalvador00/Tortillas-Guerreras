import {
  mensajeError,
  useListarCobros,
  useListarIncidencias,
  useObtenerSaldoCliente,
  useRegistrarCobro,
  useRevisarIncidencia,
  type ClienteDto,
  type CobroDto,
  type EstadoIncidencia,
  type FormaPago,
  type IncidenciaDto,
  type TipoIncidencia,
} from '@tg/api-client';
import { fechaOperativa, formatoFecha, formatoFechaHora, formatoMoneda, sumarDias } from '@tg/shared';
import { AreaTexto, Aviso, Boton, BotonIcono, Campo, Dato, Datos, Dialogo, EtiquetaEstado, Selector, TablaDatos, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros, exportarCsv } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { ETIQUETAS_FORMA_PAGO, ETIQUETAS_INCIDENCIA, SelectorCliente, SelectorRuta, useRutas } from './selectores';

// ───────────────────────────── Cobros ─────────────────────────────

function DialogoCobro({ abierto, onCambiar }: { abierto: boolean; onCambiar: (a: boolean) => void }) {
  const registrar = useRegistrarCobro();
  const [cliente, setCliente] = useState<ClienteDto | null>(null);
  const [importe, setImporte] = useState('');
  const [forma, setForma] = useState<FormaPago>('Efectivo');
  const [error, setError] = useState<string | null>(null);
  const saldo = useObtenerSaldoCliente(cliente?.id ?? '', { query: { enabled: !!cliente } });
  const cerrar = () => {
    setCliente(null);
    setImporte('');
    setError(null);
    onCambiar(false);
  };
  const guardar = async () => {
    setError(null);
    const monto = Number(importe);
    if (!cliente) return setError('Elige el cliente.');
    if (!(monto > 0)) return setError('Captura un importe mayor que cero.');
    try {
      const r = await registrar.mutateAsync({ data: { clienteId: cliente.id, importe: monto, formaPago: forma } });
      toast.success(`Cobro ${r.folio} registrado. Saldo: ${formatoMoneda(r.saldoCliente)}.`);
      cerrar();
    } catch (e) {
      setError(mensajeError(e));
    }
  };
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={(a) => (a ? onCambiar(true) : cerrar())}
      titulo="Registrar cobro"
      descripcion="Se aplica a las ventas a crédito con saldo, de la más antigua a la más reciente [SUP-08]."
      acciones={
        <>
          <Boton variante="texto" onClick={cerrar}>
            Cancelar
          </Boton>
          <Boton cargando={registrar.isPending} onClick={guardar}>
            Registrar cobro
          </Boton>
        </>
      }
    >
      <div className="grid gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <SelectorCliente valor={cliente?.id} cliente={cliente} soloCredito onCambiar={setCliente} etiqueta="Cliente a crédito" />
        {saldo.data && (
          <Datos className="sm:grid-cols-3">
            <Dato etiqueta="Saldo">{formatoMoneda(saldo.data.saldo)}</Dato>
            <Dato etiqueta="Límite">{saldo.data.limiteCredito != null ? formatoMoneda(saldo.data.limiteCredito) : 'Sin límite'}</Dato>
            <Dato etiqueta="Disponible">{saldo.data.disponible != null ? formatoMoneda(saldo.data.disponible) : '—'}</Dato>
          </Datos>
        )}
        <div className="grid gap-4 sm:grid-cols-2">
          <Campo etiqueta="Importe" type="number" step="any" min="0" prefijo="$" inputMode="decimal" value={importe} onChange={(e) => setImporte(e.target.value)} />
          <Selector
            etiqueta="Forma de pago"
            opciones={[
              { valor: 'Efectivo', etiqueta: 'Efectivo' },
              { valor: 'Transferencia', etiqueta: 'Transferencia' },
            ]}
            value={forma}
            onChange={(e) => setForma(e.target.value as FormaPago)}
          />
        </div>
      </div>
    </Dialogo>
  );
}

export function Cobros() {
  const { tienePermiso } = useSesion();
  const rutas = useRutas();
  const [desde, setDesde] = useState(sumarDias(fechaOperativa(), -7));
  const [hasta, setHasta] = useState('');
  const [rutaId, setRutaId] = useState('');
  const [pagina, setPagina] = useState(1);
  const [nuevo, setNuevo] = useState(false);
  const { data, isLoading, isFetching } = useListarCobros({
    desde: desde || undefined,
    hasta: hasta || undefined,
    rutaId: rutaId || undefined,
    pagina,
    tamano: 50,
  });
  const columnas = useMemo<ColumnDef<CobroDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Cliente', accessorKey: 'cliente' },
      { header: 'Ruta', id: 'ruta', cell: ({ row }) => rutas.find((r) => r.rutaId === row.original.rutaId)?.codigo ?? 'Oficina', meta: { ocultarEnMovil: true } },
      { header: 'Forma', accessorKey: 'formaPago', cell: ({ row }) => ETIQUETAS_FORMA_PAGO[row.original.formaPago] ?? row.original.formaPago, meta: { ocultarEnMovil: true } },
      { header: 'Importe', accessorKey: 'importe', cell: ({ row }) => formatoMoneda(row.original.importe), meta: { alinear: 'fin' } },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [rutas],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Cobros"
        subtitulo="Pagos de ventas a crédito, en oficina o en ruta [SUP-08]."
        acciones={
          <>
            <Boton
              variante="secundario"
              icono="download"
              disabled={!data?.items.length}
              onClick={() =>
                exportarCsv('cobros', data?.items ?? [], [
                  { titulo: 'Folio', valor: (c) => c.folio },
                  { titulo: 'Fecha', valor: (c) => c.fechaOperativa },
                  { titulo: 'Cliente', valor: (c) => c.cliente },
                  { titulo: 'Forma de pago', valor: (c) => c.formaPago },
                  { titulo: 'Importe', valor: (c) => c.importe },
                ])
              }
            >
              Exportar CSV
            </Boton>
            {tienePermiso('ventas.cobrar') && (
              <Boton icono="add" onClick={() => setNuevo(true)}>
                Registrar cobro
              </Boton>
            )}
          </>
        }
      />
      <BarraFiltros>
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => { setDesde(e.target.value); setPagina(1); }} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => { setHasta(e.target.value); setPagina(1); }} />
        <SelectorRuta vacio="Todas" value={rutaId} onChange={(v) => { setRutaId(v); setPagina(1); }} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(c) => c.id}
        mensajeVacio="No hay cobros en el periodo."
        detalle={(c) =>
          c.aplicaciones.length ? (
            <ul className="m-0 list-none p-0 text-sm">
              {c.aplicaciones.map((a) => (
                <li key={a.ventaId} className="flex justify-between gap-4 py-1">
                  <span>Venta {a.folio}</span>
                  <span className="tabular-nums">{formatoMoneda(a.importe)}</span>
                </li>
              ))}
            </ul>
          ) : (
            <span className="text-sm text-muted">Sin ventas aplicadas (saldo a favor).</span>
          )
        }
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
      {nuevo && <DialogoCobro abierto onCambiar={setNuevo} />}
    </Pagina>
  );
}

// ───────────────────────────── Incidencias ─────────────────────────────

function textoDetalle(detalle: unknown): string {
  if (detalle == null) return '';
  if (typeof detalle !== 'object') return String(detalle);
  return Object.entries(detalle as Record<string, unknown>)
    .map(([k, v]) => `${k}: ${typeof v === 'object' ? JSON.stringify(v) : String(v)}`)
    .join(' · ');
}

function DialogoRevision({ incidencia, onCerrar }: { incidencia: IncidenciaDto | null; onCerrar: () => void }) {
  const revisar = useRevisarIncidencia();
  const [notas, setNotas] = useState('');
  const [error, setError] = useState<string | null>(null);
  if (!incidencia) return null;
  return (
    <Dialogo
      abierto
      onCambiar={(a) => !a && onCerrar()}
      titulo={ETIQUETAS_INCIDENCIA[incidencia.tipo] ?? incidencia.tipo}
      descripcion={`${incidencia.entidadTipo} ${incidencia.referencia ?? ''} · ${formatoFechaHora(incidencia.creadoEn)}`}
      acciones={
        <>
          <Boton variante="texto" onClick={onCerrar}>
            Cancelar
          </Boton>
          <Boton
            cargando={revisar.isPending}
            onClick={async () => {
              setError(null);
              try {
                await revisar.mutateAsync({ id: incidencia.id, data: { notas: notas.trim() || null } });
                toast.success('Incidencia revisada.');
                onCerrar();
              } catch (e) {
                setError(mensajeError(e));
              }
            }}
          >
            Marcar como revisada
          </Boton>
        </>
      }
    >
      <div className="grid gap-3">
        {error && <Aviso tipo="error">{error}</Aviso>}
        {textoDetalle(incidencia.detalle) && <p className="m-0 rounded-lg bg-container px-3 py-2 text-sm break-words">{textoDetalle(incidencia.detalle)}</p>}
        <AreaTexto etiqueta="Notas de la revisión (opcional)" value={notas} onChange={(e) => setNotas(e.target.value)} />
      </div>
    </Dialogo>
  );
}

export function Incidencias() {
  const [tipo, setTipo] = useState('');
  const [estado, setEstado] = useState('Abierta');
  const [desde, setDesde] = useState(sumarDias(fechaOperativa(), -30));
  const [pagina, setPagina] = useState(1);
  const [revisando, setRevisando] = useState<IncidenciaDto | null>(null);
  const { data, isLoading, isFetching } = useListarIncidencias({
    tipo: (tipo || undefined) as TipoIncidencia | undefined,
    estado: (estado || undefined) as EstadoIncidencia | undefined,
    desde: desde || undefined,
    pagina,
    tamano: 50,
  });
  const columnas = useMemo<ColumnDef<IncidenciaDto, unknown>[]>(
    () => [
      { header: 'Fecha', accessorKey: 'creadoEn', cell: ({ row }) => formatoFechaHora(row.original.creadoEn) },
      {
        header: 'Tipo',
        accessorKey: 'tipo',
        cell: ({ row }) => <span className="font-medium">{ETIQUETAS_INCIDENCIA[row.original.tipo] ?? row.original.tipo}</span>,
      },
      { header: 'Documento', id: 'documento', cell: ({ row }) => `${row.original.entidadTipo} ${row.original.referencia ?? ''}` },
      { header: 'Detalle', id: 'detalle', cell: ({ row }) => <span className="line-clamp-2 text-sm text-muted">{textoDetalle(row.original.detalle)}</span>, meta: { ocultarEnMovil: true } },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
      {
        id: 'acciones',
        header: '',
        enableSorting: false,
        meta: { alinear: 'fin' },
        cell: ({ row }) =>
          row.original.estado === 'Abierta' ? (
            <BotonIcono icono="task_alt" etiqueta="Revisar" onClick={(e) => { e.stopPropagation(); setRevisando(row.original); }} />
          ) : null,
      },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina titulo="Incidencias" subtitulo="GPS, existencia negativa en ruta, precios y totales distintos, crédito y liquidaciones." />
      <BarraFiltros>
        <Selector
          etiqueta="Tipo"
          vacio="Todos"
          opciones={Object.entries(ETIQUETAS_INCIDENCIA).map(([valor, etiqueta]) => ({ valor, etiqueta }))}
          value={tipo}
          onChange={(e) => { setTipo(e.target.value); setPagina(1); }}
        />
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={[
            { valor: 'Abierta', etiqueta: 'Abiertas' },
            { valor: 'Revisada', etiqueta: 'Revisadas' },
          ]}
          value={estado}
          onChange={(e) => { setEstado(e.target.value); setPagina(1); }}
        />
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => { setDesde(e.target.value); setPagina(1); }} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(i) => i.id}
        mensajeVacio="No hay incidencias con esos filtros."
        detalle={(i) => (
          <Datos>
            <Dato etiqueta="Revisada por">{i.revisadaPor ?? '—'}</Dato>
            <Dato etiqueta="Revisada en">{formatoFechaHora(i.revisadaEn)}</Dato>
            <Dato etiqueta="Notas" className="col-span-2">{i.notasRevision ?? '—'}</Dato>
          </Datos>
        )}
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
      <DialogoRevision key={revisando?.id} incidencia={revisando} onCerrar={() => setRevisando(null)} />
    </Pagina>
  );
}
