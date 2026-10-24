import { useNavigate } from '@tanstack/react-router';
import {
  mensajeError,
  useConfirmarEstimado,
  useGuardarEstimado,
  useListarCanales,
  useObtenerEstimado,
  useUsarReferenciaEstimado,
  type EstimadoDto,
  type TotalEstimadoDto,
} from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoFechaLarga } from '@tg/shared';
import { Aviso, Boton, Campo, DialogoConfirmacion, EstadoVacio, Pestanas, cn, toast } from '@tg/ui';
import { useEffect, useMemo, useState } from 'react';
import { BarraFiltros } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';

/** Mañana en la fecha operativa: el estimado se arma para el día siguiente. */
function manana(): string {
  const d = new Date(`${fechaOperativa()}T12:00:00`);
  d.setDate(d.getDate() + 1);
  return d.toISOString().slice(0, 10);
}

type Celdas = Record<string, Record<string, string>>;

/** RN-PRO-11/12: tabla rutas × productos del canal; en canales con pedido por ruta las celdas son el pedido global. */
function TablaEstimado({ estimado, puedeEditar }: { estimado: EstimadoDto; puedeEditar: boolean }) {
  const guardar = useGuardarEstimado();
  const usarReferencia = useUsarReferenciaEstimado();
  const [editadas, setEditadas] = useState<Celdas>({});
  const [error, setError] = useState<string | null>(null);
  useEffect(() => setEditadas({}), [estimado]);

  const valor = (rutaId: string, productoId: string) => {
    const editada = editadas[rutaId]?.[productoId];
    if (editada !== undefined) return editada;
    const fila = estimado.filas.find((f) => f.rutaId === rutaId);
    if (!fila || fila.esReferencia) return '';
    const c = fila.celdas.find((x) => x.productoId === productoId);
    return c ? String(c.cantidad) : '';
  };
  const hayCambios = Object.keys(editadas).length > 0;

  const enviar = async () => {
    setError(null);
    try {
      await guardar.mutateAsync({
        data: {
          fechaEntrega: estimado.fechaEntrega,
          canalId: estimado.canalId,
          filas: Object.entries(editadas).map(([rutaId, celdas]) => ({
            rutaId,
            celdas: Object.entries(celdas).map(([productoId, cantidad]) => ({ productoId, cantidad: Number(cantidad || 0) })),
          })),
        },
      });
      toast.success('Estimado guardado.');
    } catch (err) {
      setError(mensajeError(err));
    }
  };

  if (estimado.filas.length === 0) return <EstadoVacio icono="route" mensaje="El canal no tiene rutas activas." />;

  return (
    <div className="flex flex-col gap-3">
      {error && <Aviso tipo="error">{error}</Aviso>}
      <div className="overflow-x-auto rounded-card border border-card-border bg-card print:overflow-visible print:border-0">
        <table className="w-full border-collapse text-sm tabular-nums">
          <thead>
            <tr className="border-b border-outline-variant text-left">
              <th className="sticky left-0 bg-card px-3 py-2 font-medium">Ruta</th>
              {estimado.columnas.map((c) => (
                <th key={c.productoId} className="px-2 py-2 text-right font-medium" title={`${c.producto} (${c.unidad})`}>
                  {c.encabezado}
                  <span className="block text-xs font-normal text-muted">{c.unidad}</span>
                </th>
              ))}
              <th className="px-3 py-2 font-medium">Observaciones</th>
              <th className="px-3 py-2 print:hidden" />
            </tr>
          </thead>
          <tbody>
            {estimado.filas.map((f) => {
              const editable = puedeEditar && f.editable;
              return (
                <tr key={f.rutaId} className={cn('border-b border-outline-variant', f.esReferencia && 'text-muted italic')}>
                  <td className="sticky left-0 bg-card px-3 py-1.5">
                    <span className="block font-medium not-italic">{f.rutaCodigo}</span>
                    <span className="text-xs">{f.esReferencia ? `Referencia ${formatoFecha(estimado.fechaReferencia)}` : f.rutaNombre}</span>
                  </td>
                  {estimado.columnas.map((c) => {
                    const ref = f.esReferencia ? f.celdas.find((x) => x.productoId === c.productoId)?.cantidad : undefined;
                    return (
                      <td key={c.productoId} className="px-1 py-1 text-right">
                        {editable ? (
                          <input
                            type="number"
                            min={0}
                            step="any"
                            inputMode="decimal"
                            aria-label={`${f.rutaCodigo} ${c.encabezado}`}
                            placeholder={ref !== undefined ? formatoCantidad(ref) : ''}
                            value={valor(f.rutaId, c.productoId)}
                            onChange={(e) =>
                              setEditadas((x) => ({ ...x, [f.rutaId]: { ...x[f.rutaId], [c.productoId]: e.target.value } }))
                            }
                            className="w-16 rounded border border-outline-variant bg-transparent px-1.5 py-1 text-right print:border-0"
                          />
                        ) : f.esReferencia ? (
                          ref !== undefined ? formatoCantidad(ref) : ''
                        ) : (
                          formatoCantidad(Number(valor(f.rutaId, c.productoId) || 0)) || ''
                        )}
                      </td>
                    );
                  })}
                  <td className="max-w-64 px-3 py-1.5 text-xs">{f.observaciones}</td>
                  <td className="px-2 py-1 text-right print:hidden">
                    {puedeEditar && f.esReferencia && f.pedidoIds.length > 0 && (
                      <Boton
                        variante="texto"
                        icono="content_copy"
                        cargando={usarReferencia.isPending}
                        onClick={async () => {
                          try {
                            await usarReferencia.mutateAsync({
                              data: { fechaEntrega: estimado.fechaEntrega, canalId: estimado.canalId, rutaId: f.rutaId },
                            });
                            toast.success(`Referencia copiada a ${f.rutaCodigo}.`);
                          } catch (err) {
                            toast.error(mensajeError(err));
                          }
                        }}
                      >
                        Usar referencia
                      </Boton>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
          <tfoot className="font-medium">
            {(
              [
                ['TOTAL', (t: TotalEstimadoDto) => formatoCantidad(t.total)],
                ['CAJAS / PAQUETES', (t: TotalEstimadoDto) => t.cajas ?? '—'],
                ['REAL', (t: TotalEstimadoDto) => formatoCantidad(t.real)],
              ] as const
            ).map(([etiqueta, texto]) => (
              <tr key={etiqueta} className="border-b border-outline-variant">
                <td className="sticky left-0 bg-card px-3 py-1.5">{etiqueta}</td>
                {estimado.columnas.map((c) => {
                  const t = estimado.totales.find((x) => x.productoId === c.productoId);
                  return (
                    <td key={c.productoId} className="px-2 py-1.5 text-right text-xs">
                      {t ? texto(t) : ''}
                    </td>
                  );
                })}
                <td colSpan={2} />
              </tr>
            ))}
          </tfoot>
        </table>
      </div>
      {puedeEditar && estimado.pedidoPor === 'Ruta' && (
        <div className="flex justify-end gap-2 print:hidden">
          <Boton variante="texto" disabled={!hayCambios} onClick={() => setEditadas({})}>
            Descartar
          </Boton>
          <Boton icono="save" disabled={!hayCambios} cargando={guardar.isPending} onClick={enviar}>
            Guardar
          </Boton>
        </div>
      )}
    </div>
  );
}

export function Estimado() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const [fecha, setFecha] = useState(manana());
  const { data: canales } = useListarCanales();
  const activos = useMemo(() => (canales ?? []).filter((c) => c.activo).sort((a, b) => a.orden - b.orden), [canales]);
  const [canalId, setCanalId] = useState('');
  const canal = canalId || activos[0]?.id || '';
  const estimado = useObtenerEstimado({ fechaEntrega: fecha, canalId: canal }, { query: { enabled: !!canal } });
  const confirmar = useConfirmarEstimado();
  const [confirmando, setConfirmando] = useState(false);
  const puedeEditar = tienePermiso('ventas.pedidos');
  const capturados = estimado.data?.pedidosCapturados ?? 0;

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Estimado de producción"
        subtitulo={`Entrega ${formatoFechaLarga(fecha)}`}
        migas={[{ etiqueta: 'Producción' }, { etiqueta: 'Estimado' }]}
        acciones={
          <>
            <Boton variante="secundario" icono="print" onClick={() => window.print()}>
              Imprimir
            </Boton>
            {tienePermiso('produccion.ver') && (
              <Boton variante="secundario" icono="checklist" onClick={() => navegar({ to: '/produccion/plan-sugerido' })}>
                Plan sugerido
              </Boton>
            )}
            {puedeEditar && (
              <Boton icono="task_alt" disabled={capturados === 0} onClick={() => setConfirmando(true)}>
                Confirmar estimado
              </Boton>
            )}
          </>
        }
      />
      <BarraFiltros>
        <Campo etiqueta="Fecha de entrega" type="date" value={fecha} onChange={(e) => setFecha(e.target.value || manana())} />
      </BarraFiltros>
      {activos.length === 0 ? (
        <EstadoVacio icono="storefront" mensaje="No hay canales activos." />
      ) : (
        <Pestanas
          valor={canal}
          onCambiar={setCanalId}
          pestanas={activos.map((c) => ({
            valor: c.id,
            etiqueta: c.nombre,
            contenido:
              c.id !== canal ? null : estimado.isLoading ? (
                <span className="tg-skeleton block h-48" />
              ) : estimado.error ? (
                <Aviso tipo="error">{mensajeError(estimado.error)}</Aviso>
              ) : estimado.data ? (
                <div className="flex flex-col gap-3">
                  {c.pedidoPor === 'Cliente' && (
                    <Aviso tipo="info" className="print:hidden">
                      En {c.nombre} el pedido es por cliente: cada ruta suma los pedidos de sus clientes. Se capturan en Pedidos.
                    </Aviso>
                  )}
                  <TablaEstimado estimado={estimado.data} puedeEditar={puedeEditar} />
                </div>
              ) : null,
          }))}
        />
      )}
      <DialogoConfirmacion
        abierto={confirmando}
        onCambiar={setConfirmando}
        titulo="Confirmar estimado"
        mensaje={`Se confirman ${capturados} pedido(s) capturados del canal para el ${formatoFecha(fecha)}. Ya no se editan desde el estimado.`}
        textoConfirmar="Confirmar"
        alConfirmar={async () => {
          const r = await confirmar.mutateAsync({ params: { fechaEntrega: fecha, canalId: canal } });
          toast.success(`${r.pedidos} pedido(s) confirmados.`);
        }}
      />
    </Pagina>
  );
}
