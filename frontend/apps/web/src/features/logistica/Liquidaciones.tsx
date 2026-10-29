import { Link, useNavigate, useParams } from '@tanstack/react-router';
import {
  esErrorApi,
  mensajeError,
  useActualizarGastoLiquidacion,
  useAgregarGastoLiquidacion,
  useCapturarConteoLiquidacion,
  useCerrarLiquidacion,
  useConfirmarProductoLiquidacion,
  useRegresarLiquidacion,
  useRevisarLiquidacion,
  useListarLiquidaciones,
  useListarMotivosMerma,
  useObtenerLiquidacion,
  type DestinoSobrante,
  type EstadoLiquidacion,
  type EtapaLiquidacion,
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
  Dialogo,
  DialogoConfirmacion,
  EtiquetaEstado,
  Pestanas,
  Selector,
  TablaDatos,
  Tarjeta,
  TarjetaIndicador,
  cn,
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useEffect, useMemo, useState } from 'react';
import { BarraFiltros, Contador } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { SelectorRuta } from './comunes-logistica';
import { DialogoNuevoCargo, TablaCargos } from './CuentaChoferes';
import { capturasIniciales, diferencia, llavePartida, resumenCierre, type CapturaPartida } from './liquidacion';

const VACIO_PESTANA: Record<string, string> = {
  todas: 'No hay liquidaciones en ese periodo.',
  PorConfirmarProducto: 'No hay liquidaciones por confirmar producto.',
  PorRevisar: 'No hay liquidaciones por revisar.',
  PorCerrar: 'No hay liquidaciones por cerrar.',
};

export function Liquidaciones() {
  const navegar = useNavigate();
  const hoy = fechaOperativa();
  const [pestana, setPestana] = useState('todas');
  const [desde, setDesde] = useState(sumarDias(hoy, -7));
  const [hasta, setHasta] = useState(hoy);
  const [rutaId, setRutaId] = useState('');
  const [estado, setEstado] = useState('');
  const [pagina, setPagina] = useState(1);
  const todas = pestana === 'todas';
  // Cada pestaña es una etapa (RN-LOG-20): lo que espera a un área, sin importar la fecha.
  const { data, isLoading, isFetching } = useListarLiquidaciones({
    desde: todas ? desde || undefined : undefined,
    hasta: todas ? hasta || undefined : undefined,
    rutaId: rutaId || undefined,
    estado: todas ? ((estado || undefined) as EstadoLiquidacion | undefined) : undefined,
    etapa: todas ? undefined : (pestana as EtapaLiquidacion),
    pagina,
    tamano: 50,
  });
  // Los contadores muestran todo lo pendiente, sin importar los filtros.
  const porConfirmar = useListarLiquidaciones({ etapa: 'PorConfirmarProducto', tamano: 1 });
  const porRevisar = useListarLiquidaciones({ etapa: 'PorRevisar', tamano: 1 });
  const porCerrar = useListarLiquidaciones({ etapa: 'PorCerrar', tamano: 1 });
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

  const contenido = (
    <>
      <BarraFiltros>
        {todas && (
          <>
            <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => (setDesde(e.target.value), setPagina(1))} />
            <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => (setHasta(e.target.value), setPagina(1))} />
          </>
        )}
        <SelectorRuta vacio="Todas" value={rutaId} onChange={(v) => (setRutaId(v), setPagina(1))} />
        {todas && (
          <Selector
            etiqueta="Estado"
            vacio="Todos"
            opciones={[
              { valor: 'Abierta', etiqueta: 'Abierta' },
              { valor: 'ProductoConfirmado', etiqueta: 'Producto confirmado' },
              { valor: 'Revisada', etiqueta: 'Revisada' },
              { valor: 'Cerrada', etiqueta: 'Cerrada' },
            ]}
            value={estado}
            onChange={(e) => (setEstado(e.target.value), setPagina(1))}
          />
        )}
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(l) => l.id}
        alClicFila={(l) => navegar({ to: '/logistica/liquidaciones/$id', params: { id: l.id } })}
        mensajeVacio={VACIO_PESTANA[pestana]}
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </>
  );

  return (
    <Pagina>
      <EncabezadoPagina titulo="Liquidaciones" subtitulo="Cierre diario de cada ruta: almacén confirma el producto, el supervisor revisa y liquidaciones cierra el dinero." />
      <Pestanas
        estirar
        valor={pestana}
        onCambiar={(v) => (setPestana(v), setPagina(1))}
        pestanas={[
          { valor: 'todas', etiqueta: 'Todas', contenido },
          {
            valor: 'PorConfirmarProducto',
            etiqueta: (
              <>
                Por confirmar producto
                <Contador valor={porConfirmar.data?.total} />
              </>
            ),
            contenido,
          },
          {
            valor: 'PorRevisar',
            etiqueta: (
              <>
                Por revisar
                <Contador valor={porRevisar.data?.total} />
              </>
            ),
            contenido,
          },
          {
            valor: 'PorCerrar',
            etiqueta: (
              <>
                Por cerrar
                <Contador valor={porCerrar.data?.total} />
              </>
            ),
            contenido,
          },
        ]}
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
  const cerrada = l.estado === 'Cerrada';
  const completo = l.formato === 'Completo';
  // RN-LOG-20: cada etapa la opera un rol distinto.
  const editable = abierta && tienePermiso('logistica.liquidar_producto');
  const puedeRevisar = tienePermiso('logistica.revisar_liquidacion');
  const puedeCerrar =
    tienePermiso('logistica.liquidar') && (l.estado === 'Revisada' || (l.estado === 'ProductoConfirmado' && !l.requiereRevision));
  const puedeForzar = tienePermiso('logistica.forzar_cierre');
  const capturar = useCapturarConteoLiquidacion();
  const confirmarProducto = useConfirmarProductoLiquidacion();
  const revisar = useRevisarLiquidacion();
  const cerrar = useCerrarLiquidacion();
  const { data: motivos } = useListarMotivosMerma({ aplica: 'ruta', activo: true });
  const [capturas, setCapturas] = useState<Record<string, CapturaPartida>>(() => capturasIniciales(l.partidas));
  const [efectivo, setEfectivo] = useState(l.efectivoEntregado == null ? '' : String(l.efectivoEntregado));
  const [notas, setNotas] = useState(l.notas ?? '');
  const [modificado, setModificado] = useState(false);
  const [dialogo, setDialogo] = useState<'confirmar' | 'cerrar' | 'regresar' | null>(null);
  const [forzar, setForzar] = useState(false);
  const [nuevoCargo, setNuevoCargo] = useState(false);
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
  const totalImporte = l.partidas.reduce((s, p) => s + Math.round(p.importeVenta * 100), 0) / 100;

  const guardarConteo = async () => {
    setError(null);
    await capturar.mutateAsync({
      id: l.id,
      data: {
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
              cantidadACobro: p.aplicaACobro && (c.aCobro ?? '').trim() !== '' ? Number(c.aCobro) : null,
            };
          }),
      },
    });
    setModificado(false);
  };

  const ejecutar = async (accion: () => Promise<unknown>, mensaje: string) => {
    setError(null);
    try {
      await accion();
      toast.success(mensaje);
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <>
      <EncabezadoPagina
        titulo={`Liquidación ${l.folio}`}
        subtitulo={`Ruta ${l.rutaCodigo} · ${l.rutaNombre} · ${formatoFecha(l.fechaOperativa)}${l.choferNombre ? ` · Chofer ${l.choferNombre}` : ''} · Formato ${completo ? 'completo' : 'simplificado'}`}
        migas={[{ etiqueta: 'Logística' }, { etiqueta: 'Liquidaciones', a: '/logistica/liquidaciones' }, { etiqueta: l.folio }]}
        acciones={
          <div className="flex items-center gap-2">
            <Link to="/logistica/liquidaciones/$id/hoja" params={{ id: l.id }} className="text-sm font-medium text-primary">
              Hoja imprimible
            </Link>
            <EtiquetaEstado estado={l.estado} />
          </div>
        }
      />
      <div className="flex flex-col gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        {abierta && l.operacionesPendientes > 0 && (
          <Aviso tipo="advertencia" titulo="Pendientes de sincronizar">
            El teléfono de la ruta reportó {l.operacionesPendientes} operación(es) sin enviar. Pide al vendedor sincronizar antes de confirmar el
            producto (RN-LOG-09){puedeForzar ? '; si no es posible, puedes forzar la confirmación.' : '.'}
          </Aviso>
        )}
        {l.cerradaForzada && <Aviso tipo="advertencia">El producto de esta liquidación se confirmó forzado con operaciones pendientes.</Aviso>}

        <Tarjeta titulo="Etapas">
          <Datos>
            <Dato etiqueta="1. Producto (almacén)">
              {l.productoConfirmadoEn ? `${formatoFechaHora(l.productoConfirmadoEn)} · ${l.productoConfirmadoPor ?? '—'}` : 'Pendiente'}
            </Dato>
            <Dato etiqueta="2. Revisión (supervisor)">
              {l.revisadaEn
                ? `${formatoFechaHora(l.revisadaEn)} · ${l.revisadaPor ?? '—'}`
                : l.requiereRevision
                  ? 'Pendiente'
                  : 'No requerida'}
            </Dato>
            <Dato etiqueta="3. Dinero (liquidaciones)">{l.cerradaEn ? `${formatoFechaHora(l.cerradaEn)} · ${l.cerradaPor ?? '—'}` : 'Pendiente'}</Dato>
          </Datos>
        </Tarjeta>

        <Tarjeta
          titulo="Producto"
          subtitulo={
            completo
              ? 'Sobrante esperado = carga + recarga − vendido − cambios entregados + devuelto vendible. Lo no vendible ya salió como merma.'
              : 'Sobrante esperado = carga − vendido.'
          }
        >
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-sm">
              <thead>
                <tr className="h-12 border-b border-row-divider text-left text-13 text-muted">
                  <th className="px-3 font-medium">Producto</th>
                  {completo ? (
                    <>
                      <th className="px-3 text-right font-medium">Carga</th>
                      <th className="px-3 text-right font-medium">Recarga</th>
                    </>
                  ) : (
                    <th className="px-3 text-right font-medium">Carga</th>
                  )}
                  <th className="px-3 text-right font-medium">Venta</th>
                  <th className="px-3 text-right font-medium">Total $</th>
                  {completo && (
                    <>
                      <th className="px-3 text-right font-medium">Dev. vendible</th>
                      <th className="px-3 text-right font-medium">Dev. no vendible</th>
                      <th className="px-3 text-right font-medium">Cambios</th>
                    </>
                  )}
                  <th className="px-3 text-right font-medium">Esperado</th>
                  <th className="px-3 font-medium">Sobrante</th>
                  <th className="px-3 font-medium">Destino del sobrante</th>
                  <th className="px-3 text-right font-medium">Diferencia</th>
                  {l.aplicaProductoACobro && <th className="px-3 font-medium">A cobro</th>}
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
                      {completo ? (
                        <>
                          <td className="px-3 text-right tabular-nums">{formatoCantidad(p.cargadoInicial)}</td>
                          <td className="px-3 text-right tabular-nums">{formatoCantidad(p.recargado)}</td>
                        </>
                      ) : (
                        <td className="px-3 text-right tabular-nums">{formatoCantidad(p.cargado)}</td>
                      )}
                      <td className="px-3 text-right tabular-nums">{formatoCantidad(p.vendido)}</td>
                      <td className="px-3 text-right tabular-nums">{formatoMoneda(p.importeVenta)}</td>
                      {completo && (
                        <>
                          <td className="px-3 text-right tabular-nums">{formatoCantidad(p.devueltoClientesVendible)}</td>
                          <td className="px-3 text-right tabular-nums">{formatoCantidad(p.devueltoClientesNoVendible)}</td>
                          <td className="px-3 text-right tabular-nums">{formatoCantidad(p.cambiosEntregados)}</td>
                        </>
                      )}
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
                      {l.aplicaProductoACobro && (
                        <td className="w-32 px-3 py-2">
                          {!p.aplicaACobro ? (
                            <span className="text-muted">—</span>
                          ) : editable ? (
                            <Campo
                              compacto
                              etiqueta={`A cobro de ${p.producto}`}
                              type="number"
                              step="any"
                              inputMode="decimal"
                              value={c.aCobro ?? ''}
                              onChange={(e) => cambiar(k, { aCobro: e.target.value })}
                              ayuda={p.cantidadACobroSugerida > 0 ? `Sugerido ${formatoCantidad(p.cantidadACobroSugerida)} (lote antiguo)` : undefined}
                            />
                          ) : (
                            <span className="tabular-nums">{formatoCantidad(p.cantidadACobro ?? 0)}</span>
                          )}
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
              {l.partidas.length > 0 && (
                <tfoot>
                  <tr className="h-10 font-medium">
                    <td className="px-3" colSpan={completo ? 4 : 3}>
                      Total
                    </td>
                    <td className="px-3 text-right tabular-nums">{formatoMoneda(totalImporte)}</td>
                    <td colSpan={(completo ? 7 : 4) + (l.aplicaProductoACobro ? 1 : 0)} />
                  </tr>
                </tfoot>
              )}
            </table>
            {l.partidas.length === 0 && <p className="m-0 px-3 py-6 text-center text-muted">La ruta no tuvo carga este día.</p>}
          </div>
          {editable && (
            <Campo etiqueta="Notas (opcional)" className="mt-3" value={notas} onChange={(e) => (setNotas(e.target.value), setModificado(true))} />
          )}
        </Tarjeta>

        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <TarjetaIndicador icono="receipt" valor={l.ventas} titulo="Ventas del día" nota={formatoMoneda(l.totalVendido)} />
          <TarjetaIndicador icono="payments" valor={formatoMoneda(l.ventasContadoEfectivo)} titulo="Contado en efectivo" />
          <TarjetaIndicador icono="request_quote" valor={formatoMoneda(l.cobrosEfectivo)} titulo="Cobros en efectivo" />
          <TarjetaIndicador icono="savings" valor={formatoMoneda(l.efectivoEsperado)} titulo="Efectivo esperado" nota={`Menos gastos ${formatoMoneda(l.totalGastos)}`} />
        </div>

        <Tarjeta titulo="Dinero" subtitulo="Créditos, cobros, transferencias y cheques vienen de Ventas (solo lectura).">
          <div className="grid items-start gap-4 sm:grid-cols-3">
            <Dato etiqueta="Ventas a crédito">{formatoMoneda(l.totalCredito)}</Dato>
            <Dato etiqueta="Cobros del día">{formatoMoneda(l.totalCobros)}</Dato>
            <Dato etiqueta="Transferencias">{formatoMoneda(l.totalTransferencias)}</Dato>
            <Dato etiqueta="Cheques">{formatoMoneda(l.totalCheques)}</Dato>
            <Dato etiqueta="Diferencia de precios">
              <span title="Σ (precio del cliente − precio cobrado) × cantidad; positivo = cobró de menos (RN-LOG-23)">
                {formatoMoneda(l.diferenciaPrecios)}
              </span>
            </Dato>
            <Dato etiqueta="Efectivo esperado">{formatoMoneda(l.efectivoEsperado)}</Dato>
            {puedeCerrar ? (
              <Campo
                etiqueta="Efectivo entregado"
                type="number"
                step="0.01"
                inputMode="decimal"
                prefijo="$"
                value={efectivo}
                onChange={(e) => setEfectivo(e.target.value)}
              />
            ) : (
              <Dato etiqueta="Entregado">{formatoMoneda(l.efectivoEntregado)}</Dato>
            )}
            <Dato etiqueta="Diferencia">
              <ImporteDiferencia valor={puedeCerrar ? difEfectivo : l.diferenciaEfectivo} />
            </Dato>
            {!editable && l.notas && (
              <Dato etiqueta="Notas" className="sm:col-span-3">
                <span className="whitespace-pre-line">{l.notas}</span>
              </Dato>
            )}
          </div>
        </Tarjeta>

        <GastosLiquidacion liquidacion={l} editable={!cerrada && tienePermiso('logistica.liquidar')} />

        <Tarjeta
          titulo="Cargos al chofer"
          subtitulo="Producto a cobro y faltantes se generan al confirmar el producto; el faltante de efectivo, al cerrar."
          acciones={
            tienePermiso('logistica.cargos_chofer') &&
            l.choferId && (
              <Boton variante="secundario" tamano="chico" icono="add" onClick={() => setNuevoCargo(true)}>
                Agregar cargo
              </Boton>
            )
          }
        >
          <TablaCargos cargos={l.cargos} puedeCancelar={tienePermiso('logistica.cargos_chofer')} />
        </Tarjeta>

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
            <Boton icono="inventory" disabled={resumen.sinContar > 0} onClick={() => setDialogo('confirmar')}>
              Confirmar producto
            </Boton>
          </BarraAccion>
        )}
        {puedeRevisar && l.estado === 'ProductoConfirmado' && l.requiereRevision && (
          <BarraAccion resumen="El producto ya está confirmado; revisa dinero, gastos y diferencias.">
            <Boton
              icono="fact_check"
              cargando={revisar.isPending}
              onClick={() => ejecutar(() => revisar.mutateAsync({ id: l.id }), `Liquidación ${l.folio} revisada.`)}
            >
              Marcar revisada
            </Boton>
          </BarraAccion>
        )}
        {(puedeCerrar || (puedeRevisar && l.estado === 'Revisada')) && (
          <BarraAccion resumen={difEfectivo != null && difEfectivo !== 0 ? `Diferencia de efectivo ${formatoMoneda(difEfectivo)}` : 'Listo para cerrar el dinero.'}>
            {puedeRevisar && l.estado === 'Revisada' && (
              <Boton variante="secundario" icono="undo" onClick={() => setDialogo('regresar')}>
                Regresar
              </Boton>
            )}
            {puedeCerrar && (
              <Boton icono="lock" disabled={efectivoNum == null || efectivoNum < 0} onClick={() => setDialogo('cerrar')}>
                Cerrar liquidación
              </Boton>
            )}
          </BarraAccion>
        )}
      </div>

      <DialogoConfirmacion
        abierto={dialogo === 'confirmar'}
        onCambiar={(a) => !a && setDialogo(null)}
        titulo={`Confirmar producto de ${l.folio}`}
        textoConfirmar="Confirmar producto"
        mensaje="Se generan los movimientos de inventario y el almacén de la ruta queda en cero. El producto ya no se puede corregir (RN-LOG-10)."
        alConfirmar={async () => {
          try {
            if (modificado) await guardarConteo();
            await confirmarProducto.mutateAsync({ id: l.id, data: { forzar } });
            toast.success(`Producto de ${l.folio} confirmado. El almacén de la ruta quedó en cero.`);
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
        </ul>
        {l.operacionesPendientes > 0 && puedeForzar && (
          <Casilla
            etiqueta="Forzar la confirmación"
            descripcion={`Hay ${l.operacionesPendientes} operación(es) sin sincronizar; se aplicarán después con incidencia.`}
            checked={forzar}
            onChange={(e) => setForzar(e.target.checked)}
          />
        )}
      </DialogoConfirmacion>

      <DialogoConfirmacion
        abierto={dialogo === 'cerrar'}
        onCambiar={(a) => !a && setDialogo(null)}
        titulo={`Cerrar ${l.folio}`}
        textoConfirmar="Cerrar liquidación"
        mensaje={`Efectivo entregado ${formatoMoneda(efectivoNum)} contra ${formatoMoneda(l.efectivoEsperado)} esperado. La liquidación ya no se puede reabrir (RN-LOG-10).`}
        alConfirmar={async () => {
          try {
            await cerrar.mutateAsync({ id: l.id, data: { efectivoEntregado: efectivoNum } });
            toast.success(`Liquidación ${l.folio} cerrada.`);
          } catch (e) {
            if (esErrorApi(e)) throw new Error(e.message);
            throw e;
          }
        }}
      >
        {difEfectivo != null && difEfectivo !== 0 && (
          <p className="m-0 text-sm">Diferencia de efectivo: {formatoMoneda(difEfectivo)} (genera incidencia).</p>
        )}
      </DialogoConfirmacion>

      {dialogo === 'regresar' && <DialogoRegresar liquidacion={l} onCerrar={() => setDialogo(null)} />}
      {nuevoCargo && <DialogoNuevoCargo liquidacionId={l.id} onCerrar={() => setNuevoCargo(false)} />}
    </>
  );
}

/** RN-LOG-21: el supervisor regresa la liquidación con nota para corregir dinero o gastos. */
function DialogoRegresar({ liquidacion: l, onCerrar }: { liquidacion: LiquidacionDto; onCerrar: () => void }) {
  const regresar = useRegresarLiquidacion();
  const [nota, setNota] = useState('');
  const [error, setError] = useState<string | null>(null);
  return (
    <Dialogo
      abierto
      onCambiar={(a) => !a && onCerrar()}
      titulo={`Regresar ${l.folio}`}
      descripcion="Vuelve a «Producto confirmado» para corregir dinero o gastos. El producto ya no cambia."
      acciones={
        <>
          <Boton variante="texto" onClick={onCerrar}>
            Cancelar
          </Boton>
          <Boton
            cargando={regresar.isPending}
            disabled={nota.trim() === ''}
            onClick={async () => {
              setError(null);
              try {
                await regresar.mutateAsync({ id: l.id, data: { nota } });
                toast.success(`Liquidación ${l.folio} regresada.`);
                onCerrar();
              } catch (err) {
                setError(mensajeError(err));
              }
            }}
          >
            Regresar
          </Boton>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Campo etiqueta="Nota" value={nota} onChange={(e) => setNota(e.target.value)} placeholder="Qué hay que corregir" />
      </div>
    </Dialogo>
  );
}

interface CapturaGasto {
  id?: string;
  concepto: string;
  importe: string;
  comprobante: string;
  autorizado: boolean;
}

/** Gastos de ruta; los autorizados se restan del efectivo esperado (RN-LOG-08). */
function GastosLiquidacion({ liquidacion: l, editable }: { liquidacion: LiquidacionDto; editable: boolean }) {
  const agregar = useAgregarGastoLiquidacion();
  const actualizar = useActualizarGastoLiquidacion();
  const [captura, setCaptura] = useState<CapturaGasto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const guardar = async () => {
    if (!captura) return;
    setError(null);
    const data = {
      concepto: captura.concepto,
      importe: Number(captura.importe),
      comprobante: captura.comprobante || null,
      autorizado: captura.autorizado,
    };
    try {
      if (captura.id) await actualizar.mutateAsync({ id: l.id, gastoId: captura.id, data });
      else await agregar.mutateAsync({ id: l.id, data });
      toast.success('Gasto guardado.');
      setCaptura(null);
    } catch (err) {
      setError(mensajeError(err));
    }
  };
  return (
    <Tarjeta
      titulo="Gastos de ruta"
      subtitulo={`Autorizados: ${formatoMoneda(l.totalGastos)}`}
      acciones={
        editable && (
          <Boton variante="secundario" tamano="chico" icono="add" onClick={() => setCaptura({ concepto: '', importe: '', comprobante: '', autorizado: true })}>
            Agregar gasto
          </Boton>
        )
      }
    >
      {l.gastos.length === 0 ? (
        <p className="m-0 text-sm text-muted">Sin gastos capturados.</p>
      ) : (
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr className="h-10 border-b border-row-divider text-left text-13 text-muted">
              <th className="px-3 font-medium">Concepto</th>
              <th className="px-3 font-medium">Comprobante</th>
              <th className="px-3 text-right font-medium">Importe</th>
              <th className="px-3 font-medium">Autorizado</th>
              {editable && <th className="px-3" />}
            </tr>
          </thead>
          <tbody>
            {l.gastos.map((g) => (
              <tr key={g.id} className="border-b border-row-divider">
                <td className="px-3 py-2">{g.concepto}</td>
                <td className="px-3">{g.comprobante ?? '—'}</td>
                <td className="px-3 text-right tabular-nums">{formatoMoneda(g.importe)}</td>
                <td className="px-3">{g.autorizado ? 'Sí' : 'No'}</td>
                {editable && (
                  <td className="px-3 text-right">
                    <Boton
                      variante="texto"
                      tamano="chico"
                      onClick={() =>
                        setCaptura({ id: g.id, concepto: g.concepto, importe: String(g.importe), comprobante: g.comprobante ?? '', autorizado: g.autorizado })
                      }
                    >
                      Editar
                    </Boton>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {captura && (
        <Dialogo
          abierto
          onCambiar={(a) => !a && setCaptura(null)}
          titulo={captura.id ? 'Editar gasto' : 'Agregar gasto'}
          acciones={
            <>
              <Boton variante="texto" onClick={() => setCaptura(null)}>
                Cancelar
              </Boton>
              <Boton
                cargando={agregar.isPending || actualizar.isPending}
                disabled={captura.concepto.trim() === '' || !(Number(captura.importe) > 0)}
                onClick={guardar}
              >
                Guardar
              </Boton>
            </>
          }
        >
          <div className="flex flex-col gap-3">
            {error && <Aviso tipo="error">{error}</Aviso>}
            <Campo
              etiqueta="Concepto"
              value={captura.concepto}
              onChange={(e) => setCaptura({ ...captura, concepto: e.target.value })}
              placeholder="Diésel, casetas, maniobras…"
            />
            <Campo
              etiqueta="Importe"
              type="number"
              step="0.01"
              inputMode="decimal"
              prefijo="$"
              value={captura.importe}
              onChange={(e) => setCaptura({ ...captura, importe: e.target.value })}
            />
            <Campo etiqueta="Comprobante (opcional)" value={captura.comprobante} onChange={(e) => setCaptura({ ...captura, comprobante: e.target.value })} />
            <Casilla
              etiqueta="Autorizado"
              descripcion="Solo los gastos autorizados se restan del efectivo esperado."
              checked={captura.autorizado}
              onChange={(e) => setCaptura({ ...captura, autorizado: e.target.checked })}
            />
          </div>
        </Dialogo>
      )}
    </Tarjeta>
  );
}
