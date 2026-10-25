import { useNavigate, useSearch } from '@tanstack/react-router';
import {
  buscarTrazabilidad,
  mensajeError,
  useRastrearTrazabilidad,
  type DireccionTraza,
  type EventoTrazaDto,
  type NodoTrazaDto,
  type TipoEntradaTraza,
  type TrazaDto,
} from '@tg/api-client';
import { formatoCantidad, formatoFecha, formatoFechaHora } from '@tg/shared';
import {
  Aviso,
  Boton,
  BotonIcono,
  BuscadorSeleccion,
  Dato,
  Datos,
  EstadoVacio,
  Girador,
  Icono,
  Pestanas,
  Selector,
  TablaDatos,
  Tarjeta,
  TarjetaIndicador,
  cn,
  type ColumnDef,
  type OpcionBusqueda,
} from '@tg/ui';
import { useCallback, useEffect, useMemo, useRef, useState, type PointerEvent as EventoPuntero } from 'react';
import { CantidadConSigno, etiquetaMovimiento, exportarCsv } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import {
  ALTO_TARJETA,
  ANCHO_TARJETA,
  ARISTA_DIFERENCIA,
  COLUMNAS_CSV_TRAZA,
  ETIQUETA_NODO,
  ICONO_NODO,
  cadena,
  codificarEntrada,
  decodificarEntrada,
  layoutTraza,
  resumenTraza,
  type LayoutTraza,
} from './traza';

// Inventario → Trazabilidad (T-8.16, RN-INV-14 a RN-INV-17): de proveedor a cliente sobre el kardex.

interface BusquedaTraza {
  tipo?: TipoEntradaTraza;
  id?: string;
  productoId?: string;
  lote?: string;
  direccion?: DireccionTraza;
  etiqueta?: string;
}

const DIRECCIONES = [
  { valor: '', etiqueta: 'Según la búsqueda' },
  { valor: 'Origen', etiqueta: 'Hacia el origen' },
  { valor: 'Ambas', etiqueta: 'Cadena completa' },
  { valor: 'Destino', etiqueta: 'Hacia el destino' },
];

const ETIQUETA_DIRECCION: Record<DireccionTraza, string> = {
  Origen: 'hacia el origen',
  Ambas: 'cadena completa',
  Destino: 'hacia el destino',
};

const AVISOS: Record<string, string> = {
  sin_lotes: 'La búsqueda no movió lotes: no hay cadena que seguir.',
  truncado: 'El recorrido llegó al tope de niveles o nodos; el mapa muestra solo una parte (RN-INV-17).',
  componentes_sin_lote:
    'Alguna orden consumió componentes sin control de lote: aparecen en su ficha pero no se pueden rastrear hasta el proveedor (RN-INV-16).',
};

/** Botón "Rastrear" de los detalles de OC, recepción, OP, carga y venta; solo con el permiso de trazabilidad. */
export function BotonRastrear({ tipo, id, etiqueta }: { tipo: TipoEntradaTraza; id: string; etiqueta?: string }) {
  const { tienePermiso } = useSesion();
  const navegar = useNavigate();
  if (!tienePermiso('inventario.trazabilidad')) return null;
  return (
    <Boton
      variante="secundario"
      icono="account_tree"
      onClick={() => void navegar({ to: '/inventario/trazabilidad', search: { tipo, id, etiqueta } as never })}
    >
      Rastrear
    </Boton>
  );
}

export function Trazabilidad() {
  const busqueda = useSearch({ strict: false }) as BusquedaTraza;
  const navegar = useNavigate();
  const [pestana, setPestana] = useState('mapa');
  const [seleccion, setSeleccion] = useState<string | null>(null);
  const entrada = busqueda.tipo ? { ...busqueda, tipo: busqueda.tipo } : null;
  const { data: traza, isFetching, error } = useRastrearTrazabilidad(
    {
      tipo: busqueda.tipo ?? 'Lote',
      id: busqueda.id,
      productoId: busqueda.productoId,
      lote: busqueda.lote,
      direccion: busqueda.direccion,
    },
    { query: { enabled: !!entrada, retry: false, staleTime: 30_000 } },
  );

  useEffect(() => setSeleccion(null), [busqueda.tipo, busqueda.id, busqueda.productoId, busqueda.lote, busqueda.direccion]);

  const buscar = useCallback(async (texto: string): Promise<OpcionBusqueda[]> => {
    if (texto.trim().length < 2) return [];
    const opciones = await buscarTrazabilidad({ q: texto });
    return opciones.map((o) => ({ valor: codificarEntrada(o), etiqueta: o.etiqueta, detalle: o.detalle }));
  }, []);

  const ir = (nueva: BusquedaTraza) => void navegar({ to: '/inventario/trazabilidad', search: nueva as never });

  const rastrearNodo = (nodo: NodoTrazaDto) => {
    if (nodo.tipo === 'Lote' && nodo.productoId && nodo.lote)
      ir({ tipo: 'Lote', productoId: nodo.productoId, lote: nodo.lote, etiqueta: `${nodo.lote} · ${nodo.producto ?? ''}` });
    else if (nodo.documentoTipo && nodo.documentoId)
      ir({ tipo: nodo.documentoTipo as TipoEntradaTraza, id: nodo.documentoId, etiqueta: nodo.folio ?? undefined });
  };

  const actual: OpcionBusqueda | null = entrada
    ? { valor: codificarEntrada(entrada), etiqueta: traza?.entrada.etiqueta ?? busqueda.etiqueta ?? 'Búsqueda' }
    : null;

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Trazabilidad"
        subtitulo="Sigue un lote, una orden de compra o un folio desde el proveedor hasta el cliente."
        acciones={
          <Boton
            variante="secundario"
            icono="download"
            disabled={!traza?.eventos.length}
            onClick={() => traza && exportarCsv(`trazabilidad-${traza.entrada.etiqueta}`, traza.eventos, COLUMNAS_CSV_TRAZA)}
          >
            Exportar CSV
          </Boton>
        }
      />

      <Tarjeta className="mb-4">
        <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_240px]">
          <BuscadorSeleccion
            etiqueta="Lote, orden de compra o folio"
            valor={actual?.valor ?? null}
            seleccion={actual}
            buscar={buscar}
            placeholderBusqueda="Escribe al menos 2 caracteres del lote o folio…"
            onCambiar={(valor, opcion) => {
              if (!valor) return;
              const e = decodificarEntrada(valor);
              ir({ ...e, tipo: e.tipo as TipoEntradaTraza, direccion: busqueda.direccion, etiqueta: opcion?.etiqueta });
            }}
          />
          <Selector
            etiqueta="Dirección"
            value={busqueda.direccion ?? ''}
            opciones={DIRECCIONES}
            onChange={(ev) => entrada && ir({ ...busqueda, direccion: (ev.target.value || undefined) as DireccionTraza | undefined })}
          />
        </div>
      </Tarjeta>

      {!entrada && (
        <EstadoVacio
          icono="account_tree"
          titulo="Busca qué rastrear"
          mensaje="Escribe un lote (por ejemplo, el de una tortilla devuelta) o el folio de una recepción, orden de producción, carga o venta."
        />
      )}
      {entrada && isFetching && !traza && <Girador etiqueta="Armando la cadena…" />}
      {entrada && !!error && <Aviso tipo="error">{mensajeError(error)}</Aviso>}

      {traza && (
        <>
          <div className="mb-3 flex flex-wrap items-center gap-2 text-13 text-muted">
            <span>
              <span className="font-medium text-on-surface">{traza.entrada.etiqueta}</span> · {ETIQUETA_DIRECCION[traza.direccion]} ·{' '}
              {traza.nodos.length} nodos · al {formatoFechaHora(traza.al)}
            </span>
            {isFetching && <Girador />}
          </div>
          {traza.avisos.map((a) => (
            <Aviso key={a} tipo={a === 'truncado' ? 'advertencia' : 'info'} className="mb-3">
              {AVISOS[a] ?? a}
            </Aviso>
          ))}
          <Pestanas
            valor={pestana}
            onCambiar={setPestana}
            pestanas={[
              {
                valor: 'mapa',
                etiqueta: 'Mapa',
                contenido: <Mapa traza={traza} seleccion={seleccion} onSeleccion={setSeleccion} onRastrear={rastrearNodo} />,
              },
              { valor: 'resumen', etiqueta: 'Resumen', contenido: <Resumen traza={traza} /> },
              {
                valor: 'bitacora',
                etiqueta: `Bitácora (${traza.eventos.length})`,
                contenido: (
                  <Bitacora
                    eventos={traza.eventos}
                    onSeleccion={(id) => {
                      setSeleccion(id);
                      setPestana('mapa');
                    }}
                  />
                ),
              },
            ]}
          />
        </>
      )}
    </Pagina>
  );
}

// ───────────────────────────── Mapa ─────────────────────────────

function colorArista(tipo: string): string {
  if (ARISTA_DIFERENCIA.has(tipo as never)) return 'var(--tg-status-red-fg)';
  if (tipo === 'Suministro') return 'var(--tg-outline)';
  if (tipo === 'Regreso') return 'var(--tg-status-blue-fg)';
  return 'var(--tg-primary)';
}

const COLOR_NODO: Partial<Record<NodoTrazaDto['tipo'], string>> = {
  Proveedor: 'var(--tg-status-gray-fg)',
  Recepcion: 'var(--tg-status-blue-fg)',
  Produccion: 'var(--tg-status-orange-fg)',
  Envio: 'var(--tg-status-yellow-fg)',
  Venta: 'var(--tg-status-green-fg)',
  Cliente: 'var(--tg-status-green-fg)',
  Merma: 'var(--tg-status-red-fg)',
  Retorno: 'var(--tg-status-red-fg)',
};

function Mapa({
  traza,
  seleccion,
  onSeleccion,
  onRastrear,
}: {
  traza: TrazaDto;
  seleccion: string | null;
  onSeleccion: (id: string | null) => void;
  onRastrear: (nodo: NodoTrazaDto) => void;
}) {
  const layout = useMemo(() => layoutTraza(traza), [traza]);
  const resaltados = useMemo(() => (seleccion ? cadena(traza.aristas, seleccion) : null), [traza.aristas, seleccion]);
  const contenedor = useRef<HTMLDivElement>(null);
  const [zoom, setZoom] = useState(1);
  const arrastre = useRef<{ x: number; y: number; izquierda: number; arriba: number; movio: boolean } | null>(null);

  const verTodo = useCallback(() => {
    const c = contenedor.current;
    if (!c) return;
    setZoom(Math.min(1, (c.clientWidth - 16) / layout.ancho, (c.clientHeight - 16) / layout.alto));
  }, [layout]);

  useEffect(() => {
    if (!seleccion || !contenedor.current) return;
    const t = layout.tarjetas.find((x) => x.id === seleccion);
    if (!t) return;
    const c = contenedor.current;
    c.scrollTo({ left: t.x * zoom - c.clientWidth / 2 + (ANCHO_TARJETA * zoom) / 2, top: t.y * zoom - c.clientHeight / 2, behavior: 'smooth' });
    // Solo al cambiar la selección: el zoom no debe volver a centrar.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [seleccion]);

  const alPresionar = (e: EventoPuntero<HTMLDivElement>) => {
    if ((e.target as HTMLElement).closest('[data-tarjeta]')) return;
    const c = contenedor.current!;
    arrastre.current = { x: e.clientX, y: e.clientY, izquierda: c.scrollLeft, arriba: c.scrollTop, movio: false };
    c.setPointerCapture(e.pointerId);
  };
  const alMover = (e: EventoPuntero<HTMLDivElement>) => {
    const a = arrastre.current;
    if (!a) return;
    const dx = e.clientX - a.x;
    const dy = e.clientY - a.y;
    if (Math.abs(dx) + Math.abs(dy) > 3) a.movio = true;
    contenedor.current!.scrollLeft = a.izquierda - dx;
    contenedor.current!.scrollTop = a.arriba - dy;
  };
  const alSoltar = () => {
    if (arrastre.current && !arrastre.current.movio) onSeleccion(null);
    arrastre.current = null;
  };

  const nodoSeleccionado = traza.nodos.find((n) => n.id === seleccion) ?? null;

  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_320px]">
      <div className="relative overflow-hidden rounded-card border border-card-border bg-card">
        <div className="absolute top-2 right-2 z-10 flex gap-1 rounded-xl bg-card/90 p-1 shadow-card">
          <BotonIcono icono="remove" etiqueta="Alejar" onClick={() => setZoom((z) => Math.max(0.2, z / 1.25))} />
          <BotonIcono icono="add" etiqueta="Acercar" onClick={() => setZoom((z) => Math.min(2, z * 1.25))} />
          <BotonIcono icono="fit_screen" etiqueta="Ver todo" onClick={verTodo} />
        </div>
        <div
          ref={contenedor}
          className="h-[min(70vh,720px)] cursor-grab overflow-auto active:cursor-grabbing"
          onPointerDown={alPresionar}
          onPointerMove={alMover}
          onPointerUp={alSoltar}
        >
          <div style={{ width: layout.ancho * zoom, height: layout.alto * zoom }}>
            <div className="relative origin-top-left" style={{ width: layout.ancho, height: layout.alto, transform: `scale(${zoom})` }}>
              <Carriles layout={layout} />
              <svg className="pointer-events-none absolute inset-0" width={layout.ancho} height={layout.alto} aria-hidden>
                {layout.enlaces.map((e) => {
                  const apagado = resaltados && !(resaltados.has(e.desde) && resaltados.has(e.hacia));
                  return (
                    <g key={e.id} opacity={apagado ? 0.12 : 1}>
                      <path
                        d={e.d}
                        fill="none"
                        stroke={colorArista(e.tipo)}
                        strokeWidth={e.grosor}
                        strokeOpacity={e.tipo === 'Suministro' ? 0.8 : 0.45}
                        strokeDasharray={e.tipo === 'Suministro' ? '4 3' : undefined}
                      />
                      {e.etiqueta && (
                        <text x={e.etiquetaX} y={e.etiquetaY} textAnchor="middle" className="fill-[var(--tg-on-surface-variant)] text-[11px]">
                          {e.etiqueta}
                        </text>
                      )}
                    </g>
                  );
                })}
              </svg>
              {layout.tarjetas.map((t) => (
                <TarjetaNodo
                  key={t.id}
                  nodo={t.nodo}
                  x={t.x}
                  y={t.y}
                  activa={seleccion === t.id}
                  apagada={!!resaltados && !resaltados.has(t.id)}
                  entrada={esEntrada(traza, t.nodo)}
                  onClick={() => onSeleccion(seleccion === t.id ? null : t.id)}
                />
              ))}
            </div>
          </div>
        </div>
      </div>
      <Ficha nodo={nodoSeleccionado} onRastrear={onRastrear} onCerrar={() => onSeleccion(null)} />
    </div>
  );
}

function esEntrada(traza: TrazaDto, nodo: NodoTrazaDto): boolean {
  const e = traza.entrada;
  if (e.tipo === 'Lote') return nodo.tipo === 'Lote' && nodo.productoId === e.productoId && nodo.lote === e.lote;
  return !!e.id && nodo.documentoId === e.id && nodo.tipo !== 'Merma' && nodo.tipo !== 'Regreso';
}

function Carriles({ layout }: { layout: LayoutTraza }) {
  return (
    <>
      {layout.carriles.map((c) => (
        <div
          key={c.indice}
          className={cn('absolute top-0 bottom-0 border-r border-row-divider', c.indice % 2 === 1 && 'bg-[var(--tg-row-alt)]')}
          style={{ left: c.x, width: 248 }}
        >
          <div className="flex items-center gap-1.5 px-3 pt-3 text-xs font-medium text-muted">
            <Icono nombre={c.icono} tamano={16} />
            <span className="truncate">{c.etiqueta}</span>
            <span className="ml-auto tabular-nums">{c.cantidad}</span>
          </div>
        </div>
      ))}
    </>
  );
}

function TarjetaNodo({
  nodo,
  x,
  y,
  activa,
  apagada,
  entrada,
  onClick,
}: {
  nodo: NodoTrazaDto;
  x: number;
  y: number;
  activa: boolean;
  apagada: boolean;
  entrada: boolean;
  onClick: () => void;
}) {
  const color = COLOR_NODO[nodo.tipo] ?? 'var(--tg-primary)';
  const pie =
    nodo.tipo === 'Lote'
      ? `${nodo.almacen?.split(' · ')[0] ?? ''} · exist. ${formatoCantidad(nodo.existencia ?? 0)} ${nodo.unidad ?? ''}`
      : nodo.fecha
        ? formatoFecha(nodo.fecha.slice(0, 10))
        : (nodo.subtitulo ?? '');
  return (
    <button
      type="button"
      data-tarjeta
      onClick={onClick}
      aria-pressed={activa}
      className={cn(
        'absolute flex flex-col gap-0.5 overflow-hidden rounded-xl border bg-card px-3 py-2 text-left shadow-card transition-[opacity,box-shadow]',
        activa ? 'border-primary ring-2 ring-primary/40' : 'border-card-border hover:border-on-surface',
        apagada && 'opacity-30',
        entrada && !activa && 'border-primary',
      )}
      style={{ left: x, top: y, width: ANCHO_TARJETA, height: ALTO_TARJETA, borderLeft: `4px solid ${color}` }}
    >
      <span className="flex items-center gap-1 text-[11px] text-muted">
        <Icono nombre={ICONO_NODO[nodo.tipo]} tamano={14} />
        {ETIQUETA_NODO[nodo.tipo]}
        {nodo.consumoSinLote.length > 0 && <Icono nombre="warning" tamano={14} className="ml-auto text-[var(--tg-status-orange-fg)]" etiqueta="Consumo sin lote" />}
      </span>
      <span className="truncate text-13 font-medium">{nodo.titulo}</span>
      <span className="truncate text-[11px] text-muted">{nodo.tipo === 'Lote' ? nodo.subtitulo : (nodo.subtitulo ?? '')}</span>
      <span className="truncate text-[11px] text-muted">{nodo.tipo === 'Lote' ? pie : nodo.tipo === 'Proveedor' || nodo.tipo === 'Cliente' ? '' : pie}</span>
    </button>
  );
}

function Ficha({ nodo, onRastrear, onCerrar }: { nodo: NodoTrazaDto | null; onRastrear: (n: NodoTrazaDto) => void; onCerrar: () => void }) {
  if (!nodo)
    return (
      <Tarjeta className="h-fit">
        <p className="m-0 text-13 text-muted">
          Toca una tarjeta para ver su ficha y resaltar su cadena. Arrastra el fondo para moverte por el mapa.
        </p>
      </Tarjeta>
    );
  const rastreable = (nodo.tipo === 'Lote' && !!nodo.lote) || !!nodo.documentoId;
  return (
    <Tarjeta
      className="h-fit"
      titulo={
        <span className="flex items-center gap-2">
          <Icono nombre={ICONO_NODO[nodo.tipo]} />
          {nodo.titulo}
        </span>
      }
      subtitulo={nodo.tipo === 'Lote' ? nodo.subtitulo : ETIQUETA_NODO[nodo.tipo]}
      acciones={<BotonIcono icono="close" etiqueta="Cerrar ficha" onClick={onCerrar} />}
    >
      <Datos className="mb-3">
        {nodo.tipo === 'Lote' && (
          <>
            <Dato etiqueta="Almacén">{nodo.almacen ?? '—'}</Dato>
            <Dato etiqueta="Caducidad">{formatoFecha(nodo.caducidad ?? null)}</Dato>
            <Dato etiqueta="Entradas">{`${formatoCantidad(nodo.entradas ?? 0)} ${nodo.unidad ?? ''}`}</Dato>
            <Dato etiqueta="Salidas">{`${formatoCantidad(nodo.salidas ?? 0)} ${nodo.unidad ?? ''}`}</Dato>
            <Dato etiqueta="Existencia">{`${formatoCantidad(nodo.existencia ?? 0)} ${nodo.unidad ?? ''}`}</Dato>
          </>
        )}
        {nodo.estado && <Dato etiqueta="Estado">{nodo.estado}</Dato>}
        {nodo.datos.map((d) => (
          <Dato key={d.etiqueta} etiqueta={d.etiqueta}>
            {d.valor}
          </Dato>
        ))}
      </Datos>
      {nodo.consumoSinLote.length > 0 && (
        <Aviso tipo="advertencia" titulo="Consumo sin lote" className="mb-3">
          <ul className="m-0 pl-4">
            {nodo.consumoSinLote.map((c) => (
              <li key={c.productoId}>
                {c.sku} {c.producto}: {formatoCantidad(c.cantidad)} {c.unidad}
              </li>
            ))}
          </ul>
        </Aviso>
      )}
      {rastreable && (
        <Boton variante="secundario" icono="account_tree" className="w-full" onClick={() => onRastrear(nodo)}>
          Rastrear desde aquí
        </Boton>
      )}
    </Tarjeta>
  );
}

// ───────────────────────────── Resumen ─────────────────────────────

function Resumen({ traza }: { traza: TrazaDto }) {
  const r = useMemo(() => resumenTraza(traza), [traza]);
  const lista = (titulo: string, nodos: NodoTrazaDto[]) =>
    nodos.length > 0 && (
      <Tarjeta titulo={`${titulo} (${nodos.length})`}>
        <ul className="m-0 list-none space-y-1.5 p-0 text-13">
          {nodos.map((n) => (
            <li key={n.id} className="flex justify-between gap-2">
              <span className="font-medium">{n.titulo}</span>
              <span className="truncate text-muted">{n.subtitulo}</span>
            </li>
          ))}
        </ul>
      </Tarjeta>
    );
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4 xl:grid-cols-7">
        <TarjetaIndicador titulo="Proveedores" valor={String(r.proveedores.length)} icono="storefront" />
        <TarjetaIndicador titulo="Recepciones" valor={String(r.recepciones.length)} icono="receipt_long" />
        <TarjetaIndicador titulo="Lotes" valor={String(r.lotes.length)} icono="sell" />
        <TarjetaIndicador titulo="Órdenes" valor={String(r.ordenes.length)} icono="factory" />
        <TarjetaIndicador titulo="Cargas" valor={String(r.cargas.length)} icono="local_shipping" />
        <TarjetaIndicador titulo="Clientes" valor={String(r.clientes.length)} icono="store" />
        <TarjetaIndicador
          titulo="Merma"
          valor={`${formatoCantidad(r.merma.cantidad)} ${r.merma.unidad ?? ''}`.trim()}
          icono="delete_sweep"
        />
      </div>
      <Tarjeta titulo={`Lotes (${r.lotes.length})`} subtitulo="Existencia sumada de todos los almacenes donde quedó el lote.">
        <TablaDatos
          datos={r.lotes}
          idFila={(l) => `${l.productoId}|${l.lote}`}
          columnas={
            [
              { header: 'Producto', accessorKey: 'producto', cell: ({ row }) => `${row.original.sku} · ${row.original.producto}` },
              { header: 'Lote', accessorKey: 'lote' },
              { header: 'Caducidad', accessorKey: 'caducidad', cell: ({ row }) => formatoFecha(row.original.caducidad) },
              { header: 'Almacenes', id: 'almacenes', cell: ({ row }) => row.original.almacenes.map((a) => a.split(' · ')[0]).join(', ') },
              {
                header: 'Existencia',
                accessorKey: 'existencia',
                cell: ({ row }) => `${formatoCantidad(row.original.existencia)} ${row.original.unidad}`,
                meta: { alinear: 'fin' },
              },
            ] satisfies ColumnDef<(typeof r.lotes)[number], unknown>[]
          }
        />
      </Tarjeta>
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {lista('Proveedores', r.proveedores)}
        {lista('Recepciones', r.recepciones)}
        {lista('Órdenes de producción', r.ordenes)}
        {lista('Cargas a ruta', r.cargas)}
        {lista('Ventas', r.ventas)}
        {lista('Clientes', r.clientes)}
      </div>
      {r.rechazo.cantidad > 0 && (
        <Aviso tipo="advertencia">
          Devuelto por clientes: {formatoCantidad(r.rechazo.cantidad)} {r.rechazo.unidad ?? ''}
        </Aviso>
      )}
    </div>
  );
}

// ───────────────────────────── Bitácora ─────────────────────────────

function Bitacora({ eventos, onSeleccion }: { eventos: EventoTrazaDto[]; onSeleccion: (nodoId: string) => void }) {
  const columnas = useMemo<ColumnDef<EventoTrazaDto, unknown>[]>(
    () => [
      { header: 'Fecha', accessorKey: 'fechaHora', cell: ({ row }) => formatoFechaHora(row.original.fechaHora) },
      { header: 'Movimiento', accessorKey: 'tipo', cell: ({ row }) => etiquetaMovimiento(row.original.tipo) },
      { header: 'Folio', accessorKey: 'folio' },
      { header: 'Almacén', accessorKey: 'almacen', meta: { ocultarEnMovil: true } },
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block font-medium">{row.original.producto}</span>
            <span className="text-xs text-muted">{row.original.sku}</span>
          </span>
        ),
      },
      { header: 'Lote', accessorKey: 'lote', cell: ({ row }) => row.original.lote || 'Sin lote' },
      {
        header: 'Cantidad',
        accessorKey: 'cantidad',
        cell: ({ row }) => <CantidadConSigno valor={row.original.cantidad} unidad={row.original.unidad} />,
        meta: { alinear: 'fin' },
      },
    ],
    [],
  );
  return (
    <Tarjeta>
      <TablaDatos
        datos={eventos}
        columnas={columnas}
        idFila={(e) => e.id}
        alClicFila={(e) => e.nodoId && onSeleccion(e.nodoId)}
        mensajeVacio="Sin movimientos."
      />
    </Tarjeta>
  );
}
