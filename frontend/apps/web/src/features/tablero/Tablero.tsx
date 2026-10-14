import { useNavigate } from '@tanstack/react-router';
import { useObtenerTablero } from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda, formatoPorcentaje } from '@tg/shared';
import { Campo, EstadoVacio, TarjetaIndicador, Tarjeta, etiquetaEnum } from '@tg/ui';
import { useState } from 'react';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { GraficaBarras, formatoPesos } from './graficas';

/** Tablero (07-web): producción vs plan, merma, ventas por ruta y canal, alertas y pendientes del día. */
export function Tablero() {
  const navegar = useNavigate();
  const { perfil } = useSesion();
  const [fecha, setFecha] = useState(fechaOperativa());
  const { data, isLoading, error } = useObtenerTablero({ fecha });

  const producido = data?.produccion.reduce((s, p) => s + p.producido, 0) ?? 0;
  const planeado = data?.produccion.reduce((s, p) => s + p.planeado, 0) ?? 0;
  const incidencias = data?.incidenciasAbiertas.reduce((s, i) => s + i.total, 0) ?? 0;

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Tablero"
        subtitulo={`Hola, ${perfil?.nombreCompleto ?? ''}. Resumen del ${formatoFecha(fecha)}.`}
        acciones={<Campo etiqueta="Fecha operativa" type="date" value={fecha} onChange={(e) => e.target.value && setFecha(e.target.value)} />}
      />
      <EstadoConsulta cargando={isLoading} error={error}>
        {data && (
          <div className="flex flex-col gap-4">
            <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
              <TarjetaIndicador
                icono="precision_manufacturing"
                valor={planeado > 0 ? formatoPorcentaje(producido / planeado, 0) : '—'}
                titulo="Producción vs plan"
                nota={`${formatoCantidad(producido, 1)} de ${formatoCantidad(planeado, 1)} planeado`}
                onClick={() => navegar({ to: '/produccion/plan' })}
              />
              <TarjetaIndicador
                icono="delete_sweep"
                valor={data.mermaDia.porcentaje != null ? formatoPorcentaje(data.mermaDia.porcentaje, 1) : '—'}
                titulo="Merma del día"
                nota={`Mes: ${data.mermaMes.porcentaje != null ? formatoPorcentaje(data.mermaMes.porcentaje, 1) : '—'} (a costo)`}
                tono={(data.mermaDia.porcentaje ?? 0) > 0.05 ? 'alerta' : 'normal'}
              />
              <TarjetaIndicador
                icono="point_of_sale"
                valor={formatoPesos(data.ventasTotalDia)}
                titulo="Ventas del día"
                nota={`${data.ventasPorRuta.reduce((s, r) => s + r.ventas, 0)} tickets`}
                onClick={() => navegar({ to: '/ventas/ventas' })}
              />
              <TarjetaIndicador
                icono="inventory"
                valor={data.bajoMinimo.length}
                titulo="Bajo mínimo"
                nota="Productos por reabastecer"
                tono={data.bajoMinimo.length ? 'alerta' : 'bien'}
                onClick={() => navegar({ to: '/inventario/existencias' })}
              />
              <TarjetaIndicador
                icono="report"
                valor={incidencias}
                titulo="Incidencias abiertas"
                nota="GPS, precios, liquidaciones"
                tono={incidencias ? 'alerta' : 'bien'}
                onClick={() => navegar({ to: '/ventas/incidencias' })}
              />
              <TarjetaIndicador
                icono="local_shipping"
                valor={data.liquidacionesPendientes.length}
                titulo="Liquidaciones abiertas"
                nota="Rutas por cerrar"
                onClick={() => navegar({ to: '/logistica/liquidaciones' })}
              />
            </div>

            <div className="grid gap-4 lg:grid-cols-2">
              <Tarjeta titulo="Producción del día vs plan" subtitulo="Por producto, en su unidad base.">
                {data.produccion.length ? (
                  <GraficaBarras
                    descripcion="Producción planeada contra producida por producto"
                    datos={data.produccion.map((p) => ({ producto: p.sku, Planeado: p.planeado, Producido: p.producido }))}
                    categoria="producto"
                    series={[
                      { clave: 'Producido', nombre: 'Producido' },
                      { clave: 'Planeado', nombre: 'Planeado' },
                    ]}
                  />
                ) : (
                  <EstadoVacio icono="calendar_view_day" mensaje="No hay órdenes de producción para esta fecha." />
                )}
              </Tarjeta>
              <Tarjeta titulo="Ventas por ruta" subtitulo="Ventas registradas del día (con impuestos).">
                {data.ventasPorRuta.length ? (
                  <GraficaBarras
                    descripcion="Ventas por ruta"
                    datos={data.ventasPorRuta.map((r) => ({ ruta: r.grupo, Total: r.total }))}
                    categoria="ruta"
                    series={[{ clave: 'Total', nombre: 'Ventas' }]}
                    formato={formatoPesos}
                  />
                ) : (
                  <EstadoVacio icono="receipt" mensaje="Aún no hay ventas en esta fecha." />
                )}
                {data.ventasPorCanal.length > 0 && (
                  <ul className="m-0 mt-3 flex list-none flex-wrap gap-x-6 gap-y-1 p-0 text-sm">
                    {data.ventasPorCanal.map((c) => (
                      <li key={c.grupo}>
                        <span className="text-muted">{c.grupo}:</span> <strong className="tabular-nums">{formatoMoneda(c.total)}</strong>
                      </li>
                    ))}
                  </ul>
                )}
              </Tarjeta>
            </div>

            <div className="grid gap-4 lg:grid-cols-3">
              <Tarjeta titulo="Existencias bajo mínimo">
                {data.bajoMinimo.length ? (
                  <ul className="m-0 list-none divide-y divide-row-divider p-0">
                    {data.bajoMinimo.map((b) => (
                      <li key={b.productoId} className="flex items-center justify-between gap-3 py-2 text-sm">
                        <span>
                          <span className="block">{b.producto}</span>
                          <span className="text-xs text-muted">{b.sku}</span>
                        </span>
                        <span className="text-right tabular-nums">
                          <span className="block font-medium text-[var(--tg-status-red-fg)]">
                            {formatoCantidad(b.existencia)} {b.unidad}
                          </span>
                          <span className="text-xs text-muted">mín. {formatoCantidad(b.minimo)}</span>
                        </span>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <EstadoVacio icono="check_circle" mensaje="Todo por encima del mínimo." />
                )}
              </Tarjeta>
              <Tarjeta titulo="Incidencias y liquidaciones">
                {data.incidenciasAbiertas.length === 0 && data.liquidacionesPendientes.length === 0 && (
                  <EstadoVacio icono="task_alt" mensaje="Sin pendientes." />
                )}
                <ul className="m-0 list-none divide-y divide-row-divider p-0 text-sm">
                  {data.incidenciasAbiertas.map((i) => (
                    <li key={i.tipo} className="flex justify-between py-2">
                      <span>{etiquetaEnum(i.tipo)}</span>
                      <strong className="tabular-nums">{i.total}</strong>
                    </li>
                  ))}
                  {data.liquidacionesPendientes.map((l) => (
                    <li key={l.id} className="flex justify-between py-2">
                      <button
                        type="button"
                        className="cursor-pointer text-primary hover:underline"
                        onClick={() => navegar({ to: '/logistica/liquidaciones/$id', params: { id: l.id } })}
                      >
                        Liquidación {l.ruta} · {l.folio}
                      </button>
                      <span className="text-muted">{formatoFecha(l.fechaOperativa)}</span>
                    </li>
                  ))}
                </ul>
              </Tarjeta>
              <Tarjeta titulo="Ajustes manuales del mes" subtitulo="RN-INV-08: todo ajuste queda a la vista.">
                {data.ajustesDelMes.length ? (
                  <ul className="m-0 list-none divide-y divide-row-divider p-0 text-sm">
                    {data.ajustesDelMes.map((a) => (
                      <li key={a.documentoId} className="flex justify-between gap-3 py-2">
                        <span className="min-w-0">
                          <span className="block font-medium">{a.folio}</span>
                          <span className="block truncate text-xs text-muted">
                            {formatoFechaHora(a.fechaHora)} · {a.notas}
                          </span>
                        </span>
                        <span className="tabular-nums">{formatoMoneda(a.costoTotal)}</span>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <EstadoVacio icono="tune" mensaje="Sin ajustes este mes." />
                )}
              </Tarjeta>
            </div>
          </div>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
