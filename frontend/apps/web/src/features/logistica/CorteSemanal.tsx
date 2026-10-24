import { useNavigate, useParams } from '@tanstack/react-router';
import {
  clienteApi,
  mensajeError,
  useAbrirCorteChofer,
  useCerrarCorteChofer,
  useObtenerCorteChofer,
  useResumenCortesChofer,
  type CorteResumenDto,
} from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda, sumarDias } from '@tg/shared';
import { Aviso, Boton, Campo, Dato, Datos, DialogoConfirmacion, EtiquetaEstado, TablaDatos, Tarjeta, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { TablaCargos, etiquetaTipoCargo } from './CuentaChoferes';

/** RN-LOG-28: resumen de la semana por chofer; abre el corte para ver el detalle, cerrarlo o exportarlo. */
export function CorteSemanal() {
  const navegar = useNavigate();
  const [fecha, setFecha] = useState(fechaOperativa());
  const { data, isLoading } = useResumenCortesChofer({ fecha });
  const abrir = useAbrirCorteChofer();
  const [error, setError] = useState<string | null>(null);
  const ver = async (r: CorteResumenDto) => {
    setError(null);
    try {
      const corte = r.corteId ? { id: r.corteId } : await abrir.mutateAsync({ data: { choferId: r.choferId, fecha } });
      await navegar({ to: '/logistica/cortes/$id', params: { id: corte.id } });
    } catch (e) {
      setError(mensajeError(e));
    }
  };
  const columnas = useMemo<ColumnDef<CorteResumenDto, unknown>[]>(
    () => [
      { header: 'Chofer', accessorKey: 'chofer', cell: ({ row }) => <span className="font-medium">{row.original.chofer ?? '—'}</span> },
      { header: 'Ventas', accessorKey: 'totalVenta', cell: ({ row }) => formatoMoneda(row.original.totalVenta), meta: { alinear: 'fin' } },
      {
        header: 'Cargos por tipo',
        id: 'cargos',
        cell: ({ row }) =>
          row.original.cargos.length === 0 ? (
            <span className="text-muted">—</span>
          ) : (
            <span className="text-sm">
              {row.original.cargos.map((c) => (
                <span key={c.tipo} className="block">
                  {etiquetaTipoCargo(c.tipo)}: {formatoMoneda(c.importe)}
                </span>
              ))}
            </span>
          ),
        meta: { ocultarEnMovil: true },
      },
      {
        header: 'A descontar',
        accessorKey: 'totalCargos',
        cell: ({ row }) => <span className="font-medium">{formatoMoneda(row.original.totalCargos)}</span>,
        meta: { alinear: 'fin' },
      },
      {
        header: 'Liquidaciones abiertas',
        accessorKey: 'liquidacionesAbiertas',
        cell: ({ row }) => row.original.liquidacionesAbiertas || '—',
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      {
        header: 'Corte',
        accessorKey: 'estado',
        cell: ({ row }) => (row.original.estado ? <EtiquetaEstado estado={row.original.estado} /> : <span className="text-muted">Sin abrir</span>),
      },
    ],
    [],
  );
  const semana = data?.[0];
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Corte semanal"
        subtitulo={semana ? `Semana del ${formatoFecha(semana.desde)} al ${formatoFecha(semana.hasta)}` : 'Ventas y cargos por chofer en la semana.'}
        migas={[{ etiqueta: 'Logística' }, { etiqueta: 'Corte semanal' }]}
      />
      <BarraFiltros>
        <Campo etiqueta="Semana de" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
        <Boton variante="secundario" onClick={() => setFecha(sumarDias(fecha, -7))}>
          Semana anterior
        </Boton>
        <Boton variante="secundario" onClick={() => setFecha(sumarDias(fecha, 7))}>
          Semana siguiente
        </Boton>
      </BarraFiltros>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <TablaDatos
        columnas={columnas}
        datos={data}
        cargando={isLoading}
        idFila={(r) => r.choferId}
        alClicFila={ver}
        mensajeVacio="Ningún chofer tiene ventas ni cargos en esa semana."
      />
    </Pagina>
  );
}

/** Detalle del corte (imprimible): ventas por día y producto, cargos y total a descontar (RN-LOG-30). */
export function CorteDetalle() {
  const { id } = useParams({ strict: false }) as { id: string };
  const { tienePermiso } = useSesion();
  const consulta = useObtenerCorteChofer(id);
  const cerrar = useCerrarCorteChofer();
  const [confirmar, setConfirmar] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const c = consulta.data;
  const exportar = async () => {
    setError(null);
    try {
      const blob = await clienteApi<Blob>({ url: `/api/v1/logistica/cortes-chofer/${id}/exportar`, method: 'GET', responseType: 'blob' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${c?.folio ?? 'corte'}.csv`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      setError(mensajeError(e));
    }
  };
  return (
    <Pagina>
      <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
        {c && (
          <>
            <div className="print:hidden">
              <EncabezadoPagina
                titulo={`Corte ${c.folio}`}
                subtitulo={`${c.chofer ?? '—'} · ${formatoFecha(c.desde)} al ${formatoFecha(c.hasta)}`}
                migas={[{ etiqueta: 'Logística' }, { etiqueta: 'Corte semanal', a: '/logistica/cortes' }, { etiqueta: c.folio }]}
                acciones={
                  <div className="flex flex-wrap items-center gap-2">
                    <EtiquetaEstado estado={c.estado} />
                    <Boton variante="secundario" icono="download" onClick={exportar}>
                      Exportar CSV
                    </Boton>
                    <Boton variante="secundario" icono="print" onClick={() => window.print()}>
                      Imprimir
                    </Boton>
                    {c.estado === 'Abierto' && tienePermiso('logistica.cargos_chofer') && (
                      <Boton icono="lock" disabled={c.liquidacionesAbiertas > 0} onClick={() => setConfirmar(true)}>
                        Cerrar corte
                      </Boton>
                    )}
                  </div>
                }
              />
            </div>
            <h1 className="m-0 mb-2 hidden text-lg font-bold print:block">
              Corte {c.folio} · {c.chofer} · {formatoFecha(c.desde)} al {formatoFecha(c.hasta)}
            </h1>
            <div className="flex flex-col gap-4">
              {error && <Aviso tipo="error">{error}</Aviso>}
              {c.estado === 'Abierto' && c.liquidacionesAbiertas > 0 && (
                <Aviso tipo="advertencia">
                  El chofer tiene {c.liquidacionesAbiertas} liquidación(es) sin cerrar en la semana; el corte no se puede cerrar (LOG-024).
                </Aviso>
              )}
              <Tarjeta>
                <Datos>
                  <Dato etiqueta="Ventas de la semana">{formatoMoneda(c.totalVenta)}</Dato>
                  {c.cargosPorTipo.map((t) => (
                    <Dato key={t.tipo} etiqueta={etiquetaTipoCargo(t.tipo)}>
                      {formatoMoneda(t.importe)}
                    </Dato>
                  ))}
                  <Dato etiqueta="Total a descontar">{formatoMoneda(c.totalCargos)}</Dato>
                  {c.cerradoEn && <Dato etiqueta="Cerrado">{`${formatoFechaHora(c.cerradoEn)} · ${c.cerradoPor ?? '—'}`}</Dato>}
                </Datos>
              </Tarjeta>
              <Tarjeta titulo="Ventas por día y producto">
                <table className="w-full border-collapse text-sm">
                  <thead>
                    <tr className="h-10 border-b border-row-divider text-left text-13 text-muted">
                      <th className="px-3 font-medium">Fecha</th>
                      <th className="px-3 font-medium">Producto</th>
                      <th className="px-3 text-right font-medium">Cantidad</th>
                      <th className="px-3 text-right font-medium">Importe</th>
                    </tr>
                  </thead>
                  <tbody>
                    {c.ventas.map((v) => (
                      <tr key={`${v.fecha}|${v.productoId}`} className="border-b border-row-divider">
                        <td className="px-3 py-1.5">{formatoFecha(v.fecha)}</td>
                        <td className="px-3">
                          {v.sku} · {v.producto}
                        </td>
                        <td className="px-3 text-right tabular-nums">
                          {formatoCantidad(v.cantidadBase)} {v.unidad}
                        </td>
                        <td className="px-3 text-right tabular-nums">{formatoMoneda(v.importe)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {c.ventas.length === 0 && <p className="m-0 px-3 py-4 text-center text-muted">Sin ventas registradas.</p>}
              </Tarjeta>
              <Tarjeta titulo={c.estado === 'Abierto' ? 'Cargos que entrarán al corte' : 'Cargos aplicados'}>
                <TablaCargos cargos={c.cargos} puedeCancelar={c.estado === 'Abierto' && tienePermiso('logistica.cargos_chofer')} />
              </Tarjeta>
              <footer className="mt-12 hidden grid-cols-2 gap-8 print:grid">
                {['Chofer', 'Crédito y cobranza'].map((rol) => (
                  <div key={rol} className="border-t border-black pt-1 text-center">
                    {rol}
                  </div>
                ))}
              </footer>
            </div>
            <DialogoConfirmacion
              abierto={confirmar}
              onCambiar={setConfirmar}
              titulo={`Cerrar corte ${c.folio}`}
              textoConfirmar="Cerrar corte"
              mensaje={`Los ${c.cargos.length} cargo(s) por ${formatoMoneda(c.totalCargos)} pasan a «Aplicado». Un cargo posterior con fecha de esta semana entra al corte siguiente.`}
              alConfirmar={async () => {
                await cerrar.mutateAsync({ id: c.id });
                toast.success(`Corte ${c.folio} cerrado.`);
              }}
            />
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
