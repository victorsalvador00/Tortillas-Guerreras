import { useParams } from '@tanstack/react-router';
import {
  mensajeError,
  useCancelarOrdenProduccion,
  useCapturarConsumosProduccion,
  useIniciarOrdenProduccion,
  useLiberarOrdenProduccion,
  useObtenerOrdenProduccion,
  useRegistrarMermaProduccion,
  useTerminarOrdenProduccion,
  type MovimientoOrdenDto,
  type OrdenDto,
} from '@tg/api-client';
import { formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda } from '@tg/shared';
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
  cn,
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useEffect, useMemo, useState } from 'react';
import { CantidadConSigno, SelectorAlmacenComponente, SelectorMotivo, etiquetaMovimiento } from '@/componentes/comunes';
import { BotonRastrear } from '@/features/inventario/Trazabilidad';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { costoEstimado, teoricoAjustado, variacion } from './calculos';

type Accion = 'liberar' | 'iniciar' | 'cancelar' | 'terminar';

function Diferencia({ real, teorico, unidad }: { real: number; teorico: number; unidad: string }) {
  const v = variacion(real, teorico);
  const dif = Math.round((real - teorico) * 10_000) / 10_000;
  if (v === null || dif === 0) return <span className="text-sm text-muted">Sin diferencia</span>;
  return (
    <span className={cn('text-sm font-medium', dif > 0 ? 'text-[var(--tg-status-red-fg)]' : 'text-[var(--tg-status-green-fg)]')}>
      {dif > 0 ? '+' : ''}
      {formatoCantidad(dif)} {unidad} ({dif > 0 ? '+' : ''}
      {(v * 100).toFixed(1)} %)
    </span>
  );
}

function MovimientosOrden({ movimientos }: { movimientos: MovimientoOrdenDto[] }) {
  const columnas = useMemo<ColumnDef<MovimientoOrdenDto, unknown>[]>(
    () => [
      { header: 'Movimiento', accessorKey: 'tipo', cell: ({ row }) => etiquetaMovimiento(row.original.tipo) },
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.producto}</span>
            <span className="text-xs text-muted">
              {row.original.sku}
              {row.original.lote ? ` · Lote ${row.original.lote}` : ''}
            </span>
          </span>
        ),
      },
      { header: 'Cantidad', accessorKey: 'cantidad', cell: ({ row }) => <CantidadConSigno valor={row.original.cantidad} />, meta: { alinear: 'fin' } },
      { header: 'Costo', accessorKey: 'costoTotal', cell: ({ row }) => formatoMoneda(row.original.costoTotal), meta: { alinear: 'fin' } },
    ],
    [],
  );
  return <TablaDatos columnas={columnas} datos={movimientos} idFila={(m) => m.id} ordenable={false} mensajeVacio="Sin movimientos." />;
}

/** Captura de la orden en planta (tableta): una columna, teclado numérico y botones grandes. */
function Captura({ orden }: { orden: OrdenDto }) {
  const { tienePermiso } = useSesion();
  const liberar = useLiberarOrdenProduccion();
  const iniciar = useIniciarOrdenProduccion();
  const cancelar = useCancelarOrdenProduccion();
  const capturar = useCapturarConsumosProduccion();
  const registrarMerma = useRegistrarMermaProduccion();
  const terminar = useTerminarOrdenProduccion();
  const [accion, setAccion] = useState<Accion | null>(null);
  const [reales, setReales] = useState<Record<string, string>>({});
  const [almacenes, setAlmacenes] = useState<Record<string, string>>({});
  const [producida, setProducida] = useState('');
  const [notasCierre, setNotasCierre] = useState('');
  const [merma, setMerma] = useState({ productoId: orden.productoId, motivoMermaId: '', cantidad: '', notas: '' });
  const [errorMerma, setErrorMerma] = useState<string | null>(null);

  useEffect(() => {
    setReales(Object.fromEntries(orden.consumos.map((c) => [c.productoId, c.cantidadRealBase != null ? String(c.cantidadRealBase) : ''])));
    setAlmacenes(Object.fromEntries(orden.consumos.map((c) => [c.productoId, c.almacenId])));
  }, [orden.consumos]);

  const enProceso = orden.estado === 'EnProceso';
  const opera = enProceso && tienePermiso('produccion.operar');
  const cierra = enProceso && tienePermiso('produccion.cerrar');
  const planea = tienePermiso('produccion.planear');
  // RN-PRO-11: el almacén de cada componente se puede cambiar hasta terminar.
  const editaAlmacen = (orden.estado === 'Liberada' || enProceso) && tienePermiso('produccion.operar');
  const producidaNum = Number(producida) > 0 ? Number(producida) : (orden.cantidadProducida ?? null);

  const filas = orden.consumos.map((c) => {
    const teorico = teoricoAjustado(c.cantidadTeoricaBase, orden.cantidadPlaneada, producidaNum);
    const capturado = reales[c.productoId];
    const real = capturado !== undefined && capturado !== '' ? Number(capturado) : (c.cantidadRealBase ?? teorico);
    return { c, teorico, real };
  });
  const costo = costoEstimado(
    filas.map((f) => ({ real: f.real, costoUnitario: f.c.costoUnitario })),
    producidaNum ?? orden.cantidadPlaneada,
  );
  const cambioReal = (c: OrdenDto['consumos'][number]) => (reales[c.productoId] ?? '') !== (c.cantidadRealBase != null ? String(c.cantidadRealBase) : '');
  const cambioAlmacen = (c: OrdenDto['consumos'][number]) => !!almacenes[c.productoId] && almacenes[c.productoId] !== c.almacenId;
  const cambios = orden.consumos.some((c) => cambioReal(c) || cambioAlmacen(c));

  const guardarConsumos = async () => {
    const consumos = orden.consumos
      .filter((c) => (cambioReal(c) && reales[c.productoId] !== '') || cambioAlmacen(c))
      .map((c) => ({
        productoId: c.productoId,
        cantidadRealBase: cambioReal(c) && reales[c.productoId] !== '' ? Number(reales[c.productoId]) : null,
        almacenId: cambioAlmacen(c) ? almacenes[c.productoId] : null,
      }));
    await capturar.mutateAsync({ id: orden.id, data: { consumos } });
  };

  const ejecutar = async (a: Accion) => {
    if (a === 'liberar') await liberar.mutateAsync({ id: orden.id });
    if (a === 'iniciar') await iniciar.mutateAsync({ id: orden.id });
    if (a === 'cancelar') await cancelar.mutateAsync({ id: orden.id });
    if (a === 'terminar') {
      if (cambios) await guardarConsumos();
      await terminar.mutateAsync({ id: orden.id, data: { cantidadProducida: Number(producida), notas: notasCierre || null } });
    }
    toast.success(
      { liberar: 'Orden liberada: lote asignado y consumos teóricos calculados.', iniciar: 'Orden en proceso.', cancelar: 'Orden cancelada.', terminar: 'Orden terminada. Revisa los movimientos generados.' }[a],
    );
  };

  const agregarMerma = async () => {
    setErrorMerma(null);
    if (!merma.motivoMermaId || !(Number(merma.cantidad) > 0)) {
      setErrorMerma('Elige el motivo y captura la cantidad.');
      return;
    }
    try {
      await registrarMerma.mutateAsync({
        id: orden.id,
        data: { productoId: merma.productoId, motivoMermaId: merma.motivoMermaId, cantidadBase: Number(merma.cantidad), notas: merma.notas || null },
      });
      setMerma((m) => ({ ...m, cantidad: '', notas: '' }));
      toast.success('Merma registrada.');
    } catch (e) {
      setErrorMerma(mensajeError(e));
    }
  };

  const productosMerma = [
    { valor: orden.productoId, etiqueta: `${orden.producto} (producto)` },
    ...orden.consumos.map((c) => ({ valor: c.productoId, etiqueta: c.producto })),
  ];

  return (
    <div className="flex flex-col gap-4">
      {orden.estado === 'Planeada' && (
        <Aviso tipo="info">Al liberar se fija la receta v{orden.recetaVersion}, se asigna el lote y se calculan los consumos teóricos.</Aviso>
      )}

      {orden.consumos.length > 0 && (
        <Tarjeta
          titulo="Consumos"
          subtitulo={
            enProceso
              ? 'El real viene con el teórico de lo producido; corrígelo si se usó más o menos (RN-PRO-03).'
              : 'Teórico calculado al liberar (RN-PRO-02).'
          }
        >
          <div className="flex flex-col gap-3">
            {filas.map(({ c, teorico, real }) => (
              <div
                key={c.id}
                className={cn(
                  'grid items-center gap-3 rounded-xl border border-row-divider border-l-4 p-3 sm:grid-cols-[1fr_320px]',
                  real > teorico ? 'border-l-[var(--tg-status-red-fg)]' : 'border-l-[var(--tg-status-green-fg)]',
                )}
              >
                <div className="min-w-0">
                  <p className="m-0 font-medium">{c.producto}</p>
                  <p className="m-0 text-xs text-muted">
                    {c.sku} · teórico {formatoCantidad(teorico)} {c.unidad} · costo {formatoMoneda(c.costoUnitario)}/{c.unidad}
                    {!editaAlmacen && ` · sale de ${c.almacen}`}
                  </p>
                  <Diferencia real={real} teorico={teorico} unidad={c.unidad} />
                </div>
                <div className="flex flex-col gap-2">
                  {editaAlmacen && (
                    <SelectorAlmacenComponente
                      productoId={c.productoId}
                      unidad={c.unidad}
                      value={almacenes[c.productoId] ?? c.almacenId}
                      onChange={(v) => setAlmacenes((a) => ({ ...a, [c.productoId]: v }))}
                    />
                  )}
                  {opera ? (
                    <Campo
                      etiqueta="Consumo real"
                      type="number"
                      step="any"
                      inputMode="decimal"
                      sufijo={c.unidad}
                      value={reales[c.productoId] ?? ''}
                      onChange={(e) => setReales((r) => ({ ...r, [c.productoId]: e.target.value }))}
                    />
                  ) : (
                    <p className="m-0 text-right text-lg tabular-nums">
                      {c.cantidadRealBase != null ? `${formatoCantidad(c.cantidadRealBase)} ${c.unidad}` : '—'}
                    </p>
                  )}
                </div>
              </div>
            ))}
          </div>
          {editaAlmacen && (
            <div className="mt-3 flex justify-end">
              <Boton variante="secundario" tamano="grande" disabled={!cambios} cargando={capturar.isPending} onClick={() => guardarConsumos().then(() => toast.success('Consumos guardados.'), (e) => toast.error(mensajeError(e)))}>
                Guardar consumos
              </Boton>
            </div>
          )}
        </Tarjeta>
      )}

      {(orden.mermas.length > 0 || opera) && (
        <Tarjeta titulo="Mermas" subtitulo="Con motivo de producción (RN-PRO-07). Se descuentan al terminar.">
          {orden.mermas.length > 0 && (
            <ul className="m-0 mb-3 list-none p-0">
              {orden.mermas.map((m) => (
                <li key={m.id} className="flex flex-wrap justify-between gap-2 border-b border-row-divider py-2 last:border-0">
                  <span>
                    <span className="block">{m.producto}</span>
                    <span className="text-xs text-muted">
                      {m.motivo}
                      {m.notas ? ` · ${m.notas}` : ''} · {formatoFechaHora(m.registradaEn)}
                    </span>
                  </span>
                  <span className="font-medium tabular-nums">
                    {formatoCantidad(m.cantidadBase)} {m.unidad}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {opera && (
            <div className="grid gap-3">
              {errorMerma && <Aviso tipo="error">{errorMerma}</Aviso>}
              <Selector etiqueta="Producto" opciones={productosMerma} value={merma.productoId} onChange={(e) => setMerma((m) => ({ ...m, productoId: e.target.value }))} />
              <SelectorMotivo aplica="produccion" value={merma.motivoMermaId} onChange={(v) => setMerma((m) => ({ ...m, motivoMermaId: v }))} />
              <Campo
                etiqueta="Cantidad (unidad base)"
                type="number"
                step="any"
                inputMode="decimal"
                value={merma.cantidad}
                onChange={(e) => setMerma((m) => ({ ...m, cantidad: e.target.value }))}
              />
              <Campo etiqueta="Notas (opcional)" value={merma.notas} onChange={(e) => setMerma((m) => ({ ...m, notas: e.target.value }))} />
              <Boton variante="secundario" tamano="grande" icono="delete_sweep" cargando={registrarMerma.isPending} onClick={agregarMerma}>
                Registrar merma
              </Boton>
            </div>
          )}
        </Tarjeta>
      )}

      {cierra && (
        <Tarjeta titulo="Terminar orden" subtitulo={`Entra con el lote ${orden.lote}; la caducidad se calcula con la vida útil del producto.`}>
          <div className="grid gap-3">
            <Campo
              etiqueta={`Cantidad producida (${orden.unidad})`}
              type="number"
              step="any"
              inputMode="decimal"
              className="[&_input]:text-2xl"
              value={producida}
              onChange={(e) => setProducida(e.target.value)}
              ayuda={`Planeado ${formatoCantidad(orden.cantidadPlaneada)} ${orden.unidad}. Lo que realmente se produjo (RN-PRO-09).`}
            />
            <Campo etiqueta="Notas de cierre (opcional)" value={notasCierre} onChange={(e) => setNotasCierre(e.target.value)} />
          </div>
        </Tarjeta>
      )}

      {orden.movimientos.length > 0 && (
        <section>
          <h2 className="m-0 mb-2 text-base font-medium">Movimientos de inventario generados</h2>
          <MovimientosOrden movimientos={orden.movimientos} />
        </section>
      )}

      {(orden.estado === 'Planeada' || orden.estado === 'Liberada' || enProceso) && (
        <BarraAccion
          resumen={
            orden.consumos.length > 0 && (
              <span className="flex flex-col">
                <strong>
                  Costo estimado {formatoMoneda(costo.total)}
                  {costo.unitario != null && ` · ${formatoMoneda(costo.unitario)} / ${orden.unidad}`}
                </strong>
                <span className="text-muted">Merma {formatoCantidad(orden.mermaTotal)} {orden.unidad}</span>
              </span>
            )
          }
        >
          {planea && (orden.estado === 'Planeada' || orden.estado === 'Liberada') && (
            <Boton variante="texto" tamano="grande" onClick={() => setAccion('cancelar')}>
              Cancelar orden
            </Boton>
          )}
          {planea && orden.estado === 'Planeada' && (
            <Boton tamano="grande" icono="lock_open" onClick={() => setAccion('liberar')}>
              Liberar
            </Boton>
          )}
          {planea && orden.estado === 'Liberada' && (
            <Boton tamano="grande" icono="play_arrow" onClick={() => setAccion('iniciar')}>
              Iniciar
            </Boton>
          )}
          {cierra && (
            <Boton tamano="grande" icono="task_alt" disabled={!(Number(producida) > 0)} onClick={() => setAccion('terminar')}>
              Terminar
            </Boton>
          )}
        </BarraAccion>
      )}

      {accion && (
        <DialogoConfirmacion
          abierto
          onCambiar={(a) => !a && setAccion(null)}
          titulo={{ liberar: 'Liberar orden', iniciar: 'Iniciar orden', cancelar: 'Cancelar orden', terminar: 'Terminar orden' }[accion]}
          mensaje={
            accion === 'terminar'
              ? `Se registran los consumos, la entrada de ${formatoCantidad(Number(producida))} ${orden.unidad} de ${orden.producto} con el lote ${orden.lote} y las mermas, en una sola operación.`
              : accion === 'cancelar'
                ? 'La orden se cancela sin movimientos de inventario.'
                : accion === 'liberar'
                  ? 'Se fija la receta, se asigna el lote y se calculan los consumos teóricos.'
                  : 'La orden pasa a En proceso para capturar consumos y mermas.'
          }
          textoConfirmar={{ liberar: 'Liberar', iniciar: 'Iniciar', cancelar: 'Cancelar orden', terminar: 'Terminar' }[accion]}
          peligro={accion === 'cancelar'}
          alConfirmar={() => ejecutar(accion)}
        />
      )}
    </div>
  );
}

export function OrdenProduccion() {
  const { id } = useParams({ strict: false }) as { id: string };
  const { data: orden, isLoading, error } = useObtenerOrdenProduccion(id);
  return (
    <Pagina ancho="medio">
      <EstadoConsulta cargando={isLoading} error={error}>
        {orden && (
          <>
            <EncabezadoPagina
              titulo={
                <span className="flex flex-wrap items-center gap-3">
                  {orden.folio} <EtiquetaEstado estado={orden.estado} />
                </span>
              }
              subtitulo={`${orden.sku} · ${orden.producto}`}
              migas={[{ etiqueta: 'Producción' }, { etiqueta: 'Órdenes de producción', a: '/produccion/ordenes' }, { etiqueta: orden.folio }]}
              acciones={orden.estado === 'Terminada' && <BotonRastrear tipo="OrdenProduccion" id={orden.id} etiqueta={orden.folio} />}
            />
            <div className="flex flex-col gap-4">
              <Tarjeta>
                <Datos>
                  <Dato etiqueta="Fecha operativa">{formatoFecha(orden.fechaOperativa)}</Dato>
                  <Dato etiqueta="Turno">{orden.turno}</Dato>
                  <Dato etiqueta="Línea">{orden.linea ?? '—'}</Dato>
                  <Dato etiqueta="Receta">
                    {orden.receta} v{orden.recetaVersion}
                  </Dato>
                  <Dato etiqueta="Planeado">
                    {formatoCantidad(orden.cantidadPlaneada)} {orden.unidad}
                  </Dato>
                  <Dato etiqueta="Producido">
                    {orden.cantidadProducida != null ? `${formatoCantidad(orden.cantidadProducida)} ${orden.unidad}` : '—'}
                  </Dato>
                  <Dato etiqueta="Lote">{orden.lote || '—'}</Dato>
                  <Dato etiqueta="Consume de / entra a">
                    {orden.almacenConsumo} → {orden.almacenDestino}
                  </Dato>
                  {orden.costoTotal != null && <Dato etiqueta="Costo total">{formatoMoneda(orden.costoTotal)}</Dato>}
                  {orden.costoUnitario != null && <Dato etiqueta="Costo unitario">{formatoMoneda(orden.costoUnitario)}</Dato>}
                  {orden.terminadaEn && <Dato etiqueta="Terminada">{formatoFechaHora(orden.terminadaEn)}</Dato>}
                  {orden.notas && <Dato etiqueta="Notas">{orden.notas}</Dato>}
                </Datos>
              </Tarjeta>
              <Captura key={orden.id} orden={orden} />
            </div>
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
