import { useNavigate, useParams } from '@tanstack/react-router';
import {
  esErrorApi,
  mensajeError,
  useCapturarConteoLiquidacion,
  useCerrarLiquidacion,
  useListarLiquidaciones,
  useListarMotivosMerma,
  useObtenerLiquidacion,
  type DestinoSobrante,
  type EstadoLiquidacion,
  type LiquidacionDto,
  type LiquidacionResumenDto,
} from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda, sumarDias } from '@tg/shared';
import {
  Aviso,
  BarraAccion,
  Boton,
  Campo,
  Casilla,
  Dato,
  Datos,
  DialogoConfirmacion,
  EtiquetaEstado,
  Selector,
  TablaDatos,
  Tarjeta,
  TarjetaIndicador,
  cn,
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useEffect, useMemo, useState } from 'react';
import { BarraFiltros } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { SelectorRuta } from './comunes-logistica';
import { capturasIniciales, diferencia, llavePartida, resumenCierre, type CapturaPartida } from './liquidacion';

export function Liquidaciones() {
  const navegar = useNavigate();
  const hoy = fechaOperativa();
  const [desde, setDesde] = useState(sumarDias(hoy, -7));
  const [hasta, setHasta] = useState(hoy);
  const [rutaId, setRutaId] = useState('');
  const [estado, setEstado] = useState('');
  const [pagina, setPagina] = useState(1);
  const { data, isLoading, isFetching } = useListarLiquidaciones({
    desde: desde || undefined,
    hasta: hasta || undefined,
    rutaId: rutaId || undefined,
    estado: (estado || undefined) as EstadoLiquidacion | undefined,
    pagina,
    tamano: 50,
  });
  const columnas = useMemo<ColumnDef<LiquidacionResumenDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Ruta', accessorKey: 'rutaCodigo' },
      { header: 'Chofer', accessorKey: 'choferNombre', cell: ({ row }) => row.original.choferNombre ?? '—', meta: { ocultarEnMovil: true } },
      { header: 'Efectivo esperado', accessorKey: 'efectivoEsperado', cell: ({ row }) => formatoMoneda(row.original.efectivoEsperado), meta: { alinear: 'fin' } },
      {
        header: 'Entregado',
        accessorKey: 'efectivoEntregado',
        cell: ({ row }) => formatoMoneda(row.original.efectivoEntregado),
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      {
        header: 'Diferencia',
        accessorKey: 'diferenciaEfectivo',
        cell: ({ row }) => <ImporteDiferencia valor={row.original.diferenciaEfectivo} />,
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina titulo="Liquidaciones" subtitulo="Cierre diario de cada ruta: producto y efectivo. Se abren al despachar la primera carga del día." />
      <BarraFiltros>
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => (setDesde(e.target.value), setPagina(1))} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => (setHasta(e.target.value), setPagina(1))} />
        <SelectorRuta vacio="Todas" value={rutaId} onChange={(v) => (setRutaId(v), setPagina(1))} />
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={[
            { valor: 'Abierta', etiqueta: 'Abierta' },
            { valor: 'Cerrada', etiqueta: 'Cerrada' },
          ]}
          value={estado}
          onChange={(e) => (setEstado(e.target.value), setPagina(1))}
        />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(l) => l.id}
        alClicFila={(l) => navegar({ to: '/logistica/liquidaciones/$id', params: { id: l.id } })}
        mensajeVacio="No hay liquidaciones en ese periodo."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </Pagina>
  );
}

function ImporteDiferencia({ valor }: { valor: number | null | undefined }) {
  if (valor == null) return <span className="text-muted">—</span>;
  return (
    <span
      className={cn(
        'font-medium tabular-nums',
        valor === 0 ? 'text-[var(--tg-status-green-fg)]' : 'text-[var(--tg-status-red-fg)]',
      )}
    >
      {valor > 0 ? '+' : ''}
      {formatoMoneda(valor)}
    </span>
  );
}

function CantidadDiferencia({ valor, unidad }: { valor: number | null; unidad: string }) {
  if (valor == null) return <span className="text-muted">Sin contar</span>;
  return (
    <span
      className={cn(
        'font-medium tabular-nums',
        valor === 0 ? 'text-[var(--tg-status-green-fg)]' : valor < 0 ? 'text-[var(--tg-status-red-fg)]' : 'text-[var(--tg-status-orange-fg)]',
      )}
    >
      {valor > 0 ? '+' : ''}
      {formatoCantidad(valor)} {unidad}
    </span>
  );
}

const DESTINOS: { valor: DestinoSobrante; etiqueta: string }[] = [
  { valor: 'Reintegrar', etiqueta: 'Reintegrar a PT' },
  { valor: 'Merma', etiqueta: 'Merma' },
  { valor: 'Reproceso', etiqueta: 'Reproceso' },
];

export function LiquidacionDetalle() {
  const { id } = useParams({ strict: false }) as { id: string };
  const consulta = useObtenerLiquidacion(id);
  return (
    <Pagina>
      <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
        {consulta.data && <FormularioLiquidacion key={consulta.data.id + consulta.data.estado} liquidacion={consulta.data} />}
      </EstadoConsulta>
    </Pagina>
  );
}

function FormularioLiquidacion({ liquidacion: l }: { liquidacion: LiquidacionDto }) {
  const { tienePermiso } = useSesion();
  const abierta = l.estado === 'Abierta';
  const editable = abierta && tienePermiso('logistica.liquidar');
  const puedeForzar = tienePermiso('logistica.forzar_cierre');
  const capturar = useCapturarConteoLiquidacion();
  const cerrar = useCerrarLiquidacion();
  const { data: motivos } = useListarMotivosMerma({ aplica: 'ruta', activo: true });
  const [capturas, setCapturas] = useState<Record<string, CapturaPartida>>(() => capturasIniciales(l.partidas));
  const [efectivo, setEfectivo] = useState(l.efectivoEntregado == null ? '' : String(l.efectivoEntregado));
  const [notas, setNotas] = useState(l.notas ?? '');
  const [modificado, setModificado] = useState(false);
  const [confirmar, setConfirmar] = useState(false);
  const [forzar, setForzar] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!modificado) setCapturas(capturasIniciales(l.partidas));
  }, [l.partidas, modificado]);

  const cambiar = (llave: string, c: Partial<CapturaPartida>) => {
    setCapturas((x) => ({ ...x, [llave]: { ...x[llave], ...c } }));
    setModificado(true);
  };

  const resumen = resumenCierre(l.partidas, capturas);
  const efectivoNum = efectivo.trim() === '' ? null : Number(efectivo);
  const difEfectivo = efectivoNum == null ? null : Math.round((efectivoNum - l.efectivoEsperado) * 100) / 100;

  const guardarConteo = async () => {
    setError(null);
    await capturar.mutateAsync({
      id: l.id,
      data: {
        efectivoEntregado: efectivoNum,
        notas: notas || null,
        partidas: l.partidas
          .filter((p) => (capturas[llavePartida(p)]?.contado ?? '').trim() !== '')
          .map((p) => {
            const c = capturas[llavePartida(p)];
            return {
              productoId: p.productoId,
              lote: p.lote,
              sobranteContado: Number(c.contado),
              destino: c.destino,
              motivoMermaId: c.motivoMermaId || null,
            };
          }),
      },
    });
    setModificado(false);
  };

  return (
    <>
      <EncabezadoPagina
        titulo={`Liquidación ${l.folio}`}
        subtitulo={`Ruta ${l.rutaCodigo} · ${l.rutaNombre} · ${formatoFecha(l.fechaOperativa)}${l.choferNombre ? ` · Chofer ${l.choferNombre}` : ''}`}
        migas={[{ etiqueta: 'Logística' }, { etiqueta: 'Liquidaciones', a: '/logistica/liquidaciones' }, { etiqueta: l.folio }]}
        acciones={<EtiquetaEstado estado={l.estado} />}
      />
      <div className="flex flex-col gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        {abierta && l.operacionesPendientes > 0 && (
          <Aviso tipo="advertencia" titulo="Pendientes de sincronizar">
            El teléfono de la ruta reportó {l.operacionesPendientes} operación(es) sin enviar. Pide al vendedor sincronizar antes de cerrar
            (RN-LOG-09){puedeForzar ? '; si no es posible, puedes forzar el cierre.' : '.'}
          </Aviso>
        )}
        {l.cerradaForzada && <Aviso tipo="advertencia">Esta liquidación se cerró forzada con operaciones pendientes.</Aviso>}

        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <TarjetaIndicador icono="receipt" valor={l.ventas} titulo="Ventas del día" nota={formatoMoneda(l.totalVendido)} />
          <TarjetaIndicador icono="payments" valor={formatoMoneda(l.ventasContadoEfectivo)} titulo="Ventas de contado" nota="En efectivo" />
          <TarjetaIndicador icono="request_quote" valor={formatoMoneda(l.cobrosEfectivo)} titulo="Cobros en efectivo" />
          <TarjetaIndicador icono="savings" valor={formatoMoneda(l.efectivoEsperado)} titulo="Efectivo esperado" nota="RN-LOG-08" />
        </div>

        <Tarjeta titulo="Producto" subtitulo="Sobrante esperado = cargado − vendido + devuelto vendible. Lo no vendible ya salió como merma.">
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-sm">
              <thead>
                <tr className="h-12 border-b border-row-divider text-left text-13 text-muted">
                  <th className="px-3 font-medium">Producto</th>
                  <th className="px-3 text-right font-medium">Cargado</th>
                  <th className="px-3 text-right font-medium">Vendido</th>
                  <th className="px-3 text-right font-medium">Dev. vendible</th>
                  <th className="px-3 text-right font-medium">Dev. no vendible</th>
                  <th className="px-3 text-right font-medium">Esperado</th>
                  <th className="px-3 font-medium">Contado</th>
                  <th className="px-3 font-medium">Destino del sobrante</th>
                  <th className="px-3 text-right font-medium">Diferencia</th>
                </tr>
              </thead>
              <tbody>
                {l.partidas.map((p) => {
                  const k = llavePartida(p);
                  const c = capturas[k] ?? { contado: '', destino: 'Reintegrar' as DestinoSobrante, motivoMermaId: '' };
                  const dif = editable ? diferencia(p.sobranteEsperado, c.contado) : (p.diferencia ?? null);
                  return (
                    <tr key={k} className="border-b border-row-divider even:bg-row-alt">
                      <td className="px-3 py-2">
                        <span className="block font-medium">{p.producto}</span>
                        <span className="text-xs text-muted">
                          {p.sku}
                          {p.lote ? ` · Lote ${p.lote}` : ''}
                        </span>
                      </td>
                      <td className="px-3 text-right tabular-nums">{formatoCantidad(p.cargado)}</td>
                      <td className="px-3 text-right tabular-nums">{formatoCantidad(p.vendido)}</td>
                      <td className="px-3 text-right tabular-nums">{formatoCantidad(p.devueltoClientesVendible)}</td>
                      <td className="px-3 text-right tabular-nums">{formatoCantidad(p.devueltoClientesNoVendible)}</td>
                      <td className="px-3 text-right font-medium tabular-nums">
                        {formatoCantidad(p.sobranteEsperado)} {p.unidad}
                      </td>
                      <td className="w-32 px-3 py-2">
                        {editable ? (
                          <Campo
                            compacto
                            etiqueta={`Sobrante contado de ${p.producto}`}
                            type="number"
                            step="any"
                            inputMode="decimal"
                            value={c.contado}
                            onChange={(e) => cambiar(k, { contado: e.target.value })}
                          />
                        ) : (
                          <span className="tabular-nums">{p.sobranteContado == null ? '—' : formatoCantidad(p.sobranteContado)}</span>
                        )}
                      </td>
                      <td className="min-w-52 px-3 py-2">
                        {editable ? (
                          <div className="flex flex-col gap-2">
                            <Selector
                              compacto
                              etiqueta="Destino del sobrante"
                              opciones={DESTINOS}
                              value={c.destino}
                              onChange={(e) => cambiar(k, { destino: e.target.value as DestinoSobrante })}
                            />
                            {c.destino === 'Merma' && (
                              <Selector
                                compacto
                                etiqueta="Motivo de merma"
                                vacio="Motivo: Otro"
                                opciones={(motivos ?? []).map((m) => ({ valor: m.id, etiqueta: m.nombre }))}
                                value={c.motivoMermaId}
                                onChange={(e) => cambiar(k, { motivoMermaId: e.target.value })}
                              />
                            )}
                          </div>
                        ) : (
                          DESTINOS.find((d) => d.valor === p.destinoSobrante)?.etiqueta
                        )}
                      </td>
                      <td className="px-3 text-right">
                        <CantidadDiferencia valor={dif} unidad={p.unidad} />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
            {l.partidas.length === 0 && <p className="m-0 px-3 py-6 text-center text-muted">La ruta no tuvo carga este día.</p>}
          </div>
        </Tarjeta>

        <Tarjeta titulo="Efectivo">
          <div className="grid items-start gap-4 sm:grid-cols-3">
            <Dato etiqueta="Esperado">{formatoMoneda(l.efectivoEsperado)}</Dato>
            {editable ? (
              <Campo
                etiqueta="Efectivo entregado"
                type="number"
                step="0.01"
                inputMode="decimal"
                prefijo="$"
                value={efectivo}
                onChange={(e) => (setEfectivo(e.target.value), setModificado(true))}
              />
            ) : (
              <Dato etiqueta="Entregado">{formatoMoneda(l.efectivoEntregado)}</Dato>
            )}
            <Dato etiqueta="Diferencia">
              <ImporteDiferencia valor={editable ? difEfectivo : l.diferenciaEfectivo} />
            </Dato>
            {editable ? (
              <Campo etiqueta="Notas (opcional)" className="sm:col-span-3" value={notas} onChange={(e) => (setNotas(e.target.value), setModificado(true))} />
            ) : (
              l.notas && <Dato etiqueta="Notas">{l.notas}</Dato>
            )}
          </div>
        </Tarjeta>

        {!abierta && (
          <Tarjeta>
            <Datos>
              <Dato etiqueta="Cerrada">{formatoFechaHora(l.cerradaEn)}</Dato>
            </Datos>
          </Tarjeta>
        )}

        {editable && (
          <BarraAccion
            resumen={
              resumen.sinContar > 0
                ? `${resumen.sinContar} partida(s) sin contar`
                : `Reintegro ${formatoCantidad(resumen.reintegrar)} · merma ${formatoCantidad(resumen.merma + resumen.reproceso)} · ${resumen.conDiferencia} diferencia(s)`
            }
          >
            <Boton
              variante="secundario"
              cargando={capturar.isPending}
              disabled={!modificado}
              onClick={() =>
                guardarConteo()
                  .then(() => toast.success('Conteo guardado.'))
                  .catch((e) => setError(mensajeError(e)))
              }
            >
              Guardar conteo
            </Boton>
            <Boton icono="lock" disabled={resumen.sinContar > 0 || efectivoNum == null} onClick={() => setConfirmar(true)}>
              Cerrar liquidación
            </Boton>
          </BarraAccion>
        )}
      </div>

      <DialogoConfirmacion
        abierto={confirmar}
        onCambiar={setConfirmar}
        titulo={`Cerrar ${l.folio}`}
        textoConfirmar="Cerrar liquidación"
        mensaje="Al cerrar se generan los movimientos y la liquidación ya no se puede reabrir (RN-LOG-10)."
        alConfirmar={async () => {
          try {
            if (modificado) await guardarConteo();
            await cerrar.mutateAsync({ id: l.id, data: { forzar } });
            toast.success(`Liquidación ${l.folio} cerrada. El almacén de la ruta quedó en cero.`);
          } catch (e) {
            if (esErrorApi(e)) throw new Error(e.message);
            throw e;
          }
        }}
      >
        <ul className="m-0 list-disc pl-5 text-sm">
          {resumen.reintegrar > 0 && <li>Reintegro a PT: {formatoCantidad(resumen.reintegrar)}</li>}
          {resumen.merma > 0 && <li>Merma: {formatoCantidad(resumen.merma)}</li>}
          {resumen.reproceso > 0 && <li>Reproceso (como merma): {formatoCantidad(resumen.reproceso)}</li>}
          {resumen.faltantes > 0 && <li>Ajuste por faltante de ruta: −{formatoCantidad(resumen.faltantes)} (con incidencia)</li>}
          {resumen.sobrantes > 0 && <li>Ajuste por sobrante: +{formatoCantidad(resumen.sobrantes)} (con incidencia)</li>}
          {difEfectivo != null && difEfectivo !== 0 && <li>Diferencia de efectivo: {formatoMoneda(difEfectivo)} (con incidencia)</li>}
        </ul>
        {l.operacionesPendientes > 0 && puedeForzar && (
          <Casilla
            etiqueta="Forzar el cierre"
            descripcion={`Hay ${l.operacionesPendientes} operación(es) sin sincronizar; se aplicarán después con incidencia.`}
            checked={forzar}
            onChange={(e) => setForzar(e.target.checked)}
          />
        )}
      </DialogoConfirmacion>
    </>
  );
}
