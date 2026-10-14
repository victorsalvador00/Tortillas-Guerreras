import { useNavigate, useParams } from '@tanstack/react-router';
import {
  listarClientes,
  mensajeError,
  useCancelarFactura,
  useCrearFactura,
  useListarFacturas,
  useListarVentas,
  useObtenerFactura,
  useReintentarFactura,
  type ConceptoDto,
  type EstadoFactura,
  type FacturaResumenDto,
  type TipoFactura,
  type VentaDto,
} from '@tg/api-client';
import { FORMAS_PAGO_SAT, fechaOperativa, formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda, sumarDias } from '@tg/shared';
import {
  Aviso,
  BarraAccion,
  Boton,
  BuscadorSeleccion,
  Campo,
  Dato,
  Datos,
  Dialogo,
  DialogoConfirmacion,
  EtiquetaEstado,
  Selector,
  TablaDatos,
  Tarjeta,
  etiquetaEnum,
  toast,
  type ColumnDef,
  type OpcionBusqueda,
} from '@tg/ui';
import { useCallback, useMemo, useState } from 'react';
import { BarraFiltros } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { MOTIVOS_CANCELACION_SAT, descargarArchivoFactura, metodoPago, totalesSeleccion, ventasFacturables } from './utilidades';

const ESTADOS: EstadoFactura[] = ['Pendiente', 'EnviadaProveedor', 'Timbrada', 'Error', 'Cancelada'];
const TIPOS: TipoFactura[] = ['Ingreso', 'Global', 'NotaCredito'];

function useBuscarClientes() {
  return useCallback(async (texto: string): Promise<OpcionBusqueda[]> => {
    const r = await listarClientes({ buscar: texto || undefined, esGenerico: false, tamano: 30 });
    return r.items.map((c) => ({ valor: c.id, etiqueta: `${c.codigo} · ${c.nombre}`, detalle: c.rfc ?? 'Sin RFC' }));
  }, []);
}

export function Facturas() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const hoy = fechaOperativa();
  const [desde, setDesde] = useState(sumarDias(hoy, -30));
  const [hasta, setHasta] = useState(hoy);
  const [estado, setEstado] = useState('');
  const [tipo, setTipo] = useState('');
  const [cliente, setCliente] = useState<OpcionBusqueda | null>(null);
  const [buscar, setBuscar] = useState('');
  const [pagina, setPagina] = useState(1);
  const buscarClientes = useBuscarClientes();
  const { data, isLoading, isFetching } = useListarFacturas({
    desde: desde || undefined,
    hasta: hasta || undefined,
    estado: (estado || undefined) as EstadoFactura | undefined,
    tipo: (tipo || undefined) as TipoFactura | undefined,
    clienteId: cliente?.valor,
    buscar: buscar || undefined,
    pagina,
    tamano: 50,
  });
  const columnas = useMemo<ColumnDef<FacturaResumenDto, unknown>[]>(
    () => [
      {
        header: 'Folio',
        id: 'folio',
        cell: ({ row }) => (
          <span className="font-medium">
            {row.original.serie}-{row.original.folio}
          </span>
        ),
      },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Tipo', accessorKey: 'tipo', cell: ({ row }) => etiquetaEnum(row.original.tipo), meta: { ocultarEnMovil: true } },
      {
        header: 'Receptor',
        accessorKey: 'receptorNombre',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.receptorNombre}</span>
            <span className="text-xs text-muted">{row.original.receptorRfc}</span>
          </span>
        ),
      },
      { header: 'Método', accessorKey: 'metodoPagoSat', meta: { ocultarEnMovil: true } },
      { header: 'Total', accessorKey: 'total', cell: ({ row }) => formatoMoneda(row.original.total), meta: { alinear: 'fin' } },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Facturas"
        subtitulo="CFDI de ventas, autofacturas y facturas globales. Proveedor de timbrado simulado hasta resolver D-06."
        acciones={
          tienePermiso('facturacion.emitir') && (
            <Boton icono="add" onClick={() => navegar({ to: '/facturacion/facturas/nueva' })}>
              Nueva factura
            </Boton>
          )
        }
      />
      <BarraFiltros>
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => (setDesde(e.target.value), setPagina(1))} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => (setHasta(e.target.value), setPagina(1))} />
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={ESTADOS.map((e) => ({ valor: e, etiqueta: etiquetaEnum(e) }))}
          value={estado}
          onChange={(e) => (setEstado(e.target.value), setPagina(1))}
        />
        <Selector
          etiqueta="Tipo"
          vacio="Todos"
          opciones={TIPOS.map((t) => ({ valor: t, etiqueta: etiquetaEnum(t) }))}
          value={tipo}
          onChange={(e) => (setTipo(e.target.value), setPagina(1))}
        />
        <BuscadorSeleccion etiqueta="Cliente" valor={cliente?.valor} seleccion={cliente} buscar={buscarClientes} onCambiar={(_, o) => (setCliente(o), setPagina(1))} />
        <Campo etiqueta="Buscar folio, RFC o UUID" type="search" value={buscar} onChange={(e) => (setBuscar(e.target.value), setPagina(1))} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(f) => f.id}
        alClicFila={(f) => navegar({ to: '/facturacion/facturas/$id', params: { id: f.id } })}
        mensajeVacio="No hay facturas con esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </Pagina>
  );
}

export function FacturaNueva() {
  const navegar = useNavigate();
  const crear = useCrearFactura();
  const buscarClientes = useBuscarClientes();
  const [cliente, setCliente] = useState<OpcionBusqueda | null>(null);
  const [email, setEmail] = useState('');
  const [seleccion, setSeleccion] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const ventas = useListarVentas(
    { clienteId: cliente?.valor, estado: 'Registrada', tamano: 200 },
    { query: { enabled: !!cliente } },
  );
  const disponibles = useMemo(() => ventasFacturables(ventas.data?.items ?? []), [ventas.data]);
  const totales = totalesSeleccion(disponibles, seleccion);
  const elegidas = disponibles.filter((v) => seleccion.has(v.id));

  const columnas = useMemo<ColumnDef<VentaDto, unknown>[]>(
    () => [
      {
        id: 'sel',
        header: '',
        enableSorting: false,
        cell: ({ row }) => (
          <input
            type="checkbox"
            aria-label={`Incluir ${row.original.folio}`}
            className="size-5 accent-[var(--tg-primary)]"
            checked={seleccion.has(row.original.id)}
            onChange={(e) =>
              setSeleccion((s) => {
                const n = new Set(s);
                if (e.target.checked) n.add(row.original.id);
                else n.delete(row.original.id);
                return n;
              })
            }
          />
        ),
      },
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Pago', id: 'pago', cell: ({ row }) => `${etiquetaEnum(row.original.condicionPago)} · ${etiquetaEnum(row.original.formaPago)}` },
      { header: 'Total', accessorKey: 'total', cell: ({ row }) => formatoMoneda(row.original.total), meta: { alinear: 'fin' } },
    ],
    [seleccion],
  );

  const facturar = async () => {
    setError(null);
    try {
      const f = await crear.mutateAsync({ data: { ventaIds: [...seleccion], email: email || null, receptor: null } });
      toast.success(`Factura ${f.serie}-${f.folio} creada; se timbra en segundo plano.`);
      await navegar({ to: '/facturacion/facturas/$id', params: { id: f.id } });
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Nueva factura"
        subtitulo="Elige el cliente y sus ventas registradas sin facturar. Los datos fiscales salen del cliente."
        migas={[{ etiqueta: 'Facturación' }, { etiqueta: 'Facturas', a: '/facturacion/facturas' }, { etiqueta: 'Nueva' }]}
      />
      <div className="flex flex-col gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Tarjeta>
          <div className="grid gap-4 sm:grid-cols-2">
            <BuscadorSeleccion
              etiqueta="Cliente"
              valor={cliente?.valor}
              seleccion={cliente}
              buscar={buscarClientes}
              onCambiar={(_, o) => (setCliente(o), setSeleccion(new Set()))}
            />
            <Campo etiqueta="Correo para enviar XML y PDF (opcional)" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
          </div>
        </Tarjeta>
        {cliente && (
          <TablaDatos
            columnas={columnas}
            datos={disponibles}
            cargando={ventas.isLoading}
            idFila={(v) => v.id}
            mensajeVacio="El cliente no tiene ventas pendientes de facturar."
          />
        )}
        <BarraAccion
          resumen={
            totales.ventas > 0
              ? `${totales.ventas} venta(s) · Subtotal ${formatoMoneda(totales.subtotal)} · IVA ${formatoMoneda(totales.iva)}${totales.ieps ? ` · IEPS ${formatoMoneda(totales.ieps)}` : ''} · Total ${formatoMoneda(totales.total)} · ${metodoPago(elegidas)}`
              : 'Selecciona las ventas a facturar.'
          }
        >
          <Boton variante="texto" onClick={() => navegar({ to: '/facturacion/facturas' })}>
            Cancelar
          </Boton>
          <Boton disabled={totales.ventas === 0} cargando={crear.isPending} onClick={facturar}>
            Facturar
          </Boton>
        </BarraAccion>
      </div>
    </Pagina>
  );
}

export function FacturaDetalle() {
  const { id } = useParams({ strict: false }) as { id: string };
  const { tienePermiso } = useSesion();
  const consulta = useObtenerFactura(id, { query: { refetchInterval: (q) => (q.state.data && ['Pendiente', 'EnviadaProveedor'].includes(q.state.data.estado) ? 5000 : false) } });
  const reintentar = useReintentarFactura();
  const cancelar = useCancelarFactura();
  const [confirmarReintento, setConfirmarReintento] = useState(false);
  const [dialogoCancelar, setDialogoCancelar] = useState(false);
  const [motivo, setMotivo] = useState('02');
  const [sustituye, setSustituye] = useState('');
  const [errorCancelar, setErrorCancelar] = useState<string | null>(null);
  const [descargando, setDescargando] = useState<'xml' | 'pdf' | null>(null);
  const f = consulta.data;

  const columnas = useMemo<ColumnDef<ConceptoDto, unknown>[]>(
    () => [
      { header: 'Clave SAT', accessorKey: 'claveProdServ', meta: { ocultarEnMovil: true } },
      { header: 'Descripción', accessorKey: 'descripcion' },
      { header: 'Cantidad', id: 'cantidad', cell: ({ row }) => `${formatoCantidad(row.original.cantidad)} ${row.original.claveUnidad}`, meta: { alinear: 'fin' } },
      { header: 'Valor unitario', accessorKey: 'valorUnitario', cell: ({ row }) => formatoMoneda(row.original.valorUnitario), meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Importe', accessorKey: 'importe', cell: ({ row }) => formatoMoneda(row.original.importe), meta: { alinear: 'fin' } },
      { header: 'IVA', accessorKey: 'iva', cell: ({ row }) => formatoMoneda(row.original.iva), meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'IEPS', accessorKey: 'ieps', cell: ({ row }) => formatoMoneda(row.original.ieps), meta: { alinear: 'fin', ocultarEnMovil: true } },
    ],
    [],
  );

  const descargar = async (tipo: 'xml' | 'pdf') => {
    if (!f) return;
    setDescargando(tipo);
    try {
      await descargarArchivoFactura(f.id, tipo, `${f.serie}-${f.folio}`);
    } catch (e) {
      toast.error(mensajeError(e));
    } finally {
      setDescargando(null);
    }
  };

  return (
    <Pagina>
      <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
        {f && (
          <>
            <EncabezadoPagina
              titulo={`Factura ${f.serie}-${f.folio}`}
              subtitulo={`${etiquetaEnum(f.tipo)} · ${f.receptorNombre} · ${formatoFecha(f.fechaOperativa)}`}
              migas={[{ etiqueta: 'Facturación' }, { etiqueta: 'Facturas', a: '/facturacion/facturas' }, { etiqueta: `${f.serie}-${f.folio}` }]}
              acciones={
                <>
                  {f.tieneXml && (
                    <Boton variante="secundario" icono="code" cargando={descargando === 'xml'} onClick={() => descargar('xml')}>
                      XML
                    </Boton>
                  )}
                  {f.tienePdf && (
                    <Boton variante="secundario" icono="picture_as_pdf" cargando={descargando === 'pdf'} onClick={() => descargar('pdf')}>
                      PDF
                    </Boton>
                  )}
                  {f.estado === 'Error' && tienePermiso('facturacion.emitir') && (
                    <Boton icono="refresh" onClick={() => setConfirmarReintento(true)}>
                      Reintentar
                    </Boton>
                  )}
                  {f.estado === 'Timbrada' && tienePermiso('facturacion.cancelar') && (
                    <Boton variante="peligro" icono="block" onClick={() => setDialogoCancelar(true)}>
                      Cancelar
                    </Boton>
                  )}
                </>
              }
            />
            <div className="flex flex-col gap-4">
              {f.estado === 'Error' && f.errorDetalle && (
                <Aviso tipo="error" titulo={`No se pudo timbrar (intento ${f.intentos})`}>
                  {f.errorDetalle}
                </Aviso>
              )}
              {['Pendiente', 'EnviadaProveedor'].includes(f.estado) && (
                <Aviso tipo="info">La factura se está timbrando; esta pantalla se actualiza sola.</Aviso>
              )}
              {f.estado === 'Cancelada' && (
                <Aviso tipo="advertencia" titulo="Factura cancelada">
                  {f.motivoCancelacion} · {formatoFechaHora(f.canceladaEn)}
                </Aviso>
              )}
              <Tarjeta>
                <Datos>
                  <Dato etiqueta="Estado">
                    <EtiquetaEstado estado={f.estado} />
                  </Dato>
                  <Dato etiqueta="UUID fiscal">{f.uuidFiscal ?? 'Sin timbrar'}</Dato>
                  <Dato etiqueta="Receptor">
                    {f.receptorNombre} · {f.receptorRfc}
                  </Dato>
                  <Dato etiqueta="Régimen / CP / Uso">
                    {f.receptorRegimen} · {f.receptorCp} · {f.usoCfdi}
                  </Dato>
                  <Dato etiqueta="Método y forma de pago">
                    {f.metodoPagoSat} · {FORMAS_PAGO_SAT[f.formaPagoSat] ?? f.formaPagoSat}
                  </Dato>
                  {f.periodicidad && (
                    <Dato etiqueta="Periodo (global)">
                      {f.periodicidad} · {f.meses}/{f.anio}
                    </Dato>
                  )}
                  <Dato etiqueta="Timbrada">{formatoFechaHora(f.timbradaEn)}</Dato>
                  <Dato etiqueta="Proveedor">{f.proveedor}</Dato>
                  <Dato etiqueta="Correo">{f.emailReceptor ?? '—'}</Dato>
                  <Dato etiqueta="Ventas">{f.ventas.map((v) => v.folio).join(', ') || '—'}</Dato>
                </Datos>
              </Tarjeta>
              <TablaDatos columnas={columnas} datos={f.conceptos} idFila={(c) => c.id} ordenable={false} />
              <Tarjeta>
                <div className="ml-auto grid max-w-xs grid-cols-2 gap-y-1 text-sm">
                  <span className="text-muted">Subtotal</span>
                  <span className="text-right tabular-nums">{formatoMoneda(f.subtotal)}</span>
                  <span className="text-muted">IEPS</span>
                  <span className="text-right tabular-nums">{formatoMoneda(f.ieps)}</span>
                  <span className="text-muted">IVA</span>
                  <span className="text-right tabular-nums">{formatoMoneda(f.iva)}</span>
                  <span className="font-medium">Total</span>
                  <span className="text-right text-base font-semibold tabular-nums">{formatoMoneda(f.total)}</span>
                </div>
              </Tarjeta>
            </div>
            <DialogoConfirmacion
              abierto={confirmarReintento}
              onCambiar={setConfirmarReintento}
              titulo="Reintentar timbrado"
              mensaje="La factura vuelve a la cola de timbrado."
              textoConfirmar="Reintentar"
              alConfirmar={async () => {
                await reintentar.mutateAsync({ id: f.id });
                toast.success('Factura enviada de nuevo a timbrar.');
              }}
            />
            <Dialogo
              abierto={dialogoCancelar}
              onCambiar={(a) => (setDialogoCancelar(a), setErrorCancelar(null))}
              titulo={`Cancelar ${f.serie}-${f.folio}`}
              descripcion="Las ventas quedan libres para facturarse de nuevo."
              ancho="chico"
              acciones={
                <>
                  <Boton variante="texto" onClick={() => setDialogoCancelar(false)}>
                    Volver
                  </Boton>
                  <Boton
                    variante="peligro"
                    cargando={cancelar.isPending}
                    disabled={motivo === '01' && !sustituye}
                    onClick={async () => {
                      setErrorCancelar(null);
                      try {
                        await cancelar.mutateAsync({ id: f.id, data: { motivo, sustituye: motivo === '01' ? sustituye : null } });
                        toast.success('Factura cancelada.');
                        setDialogoCancelar(false);
                      } catch (e) {
                        setErrorCancelar(mensajeError(e));
                      }
                    }}
                  >
                    Cancelar factura
                  </Boton>
                </>
              }
            >
              <div className="flex flex-col gap-4">
                {errorCancelar && <Aviso tipo="error">{errorCancelar}</Aviso>}
                <Selector etiqueta="Motivo SAT" opciones={MOTIVOS_CANCELACION_SAT} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
                {motivo === '01' && (
                  <Campo
                    etiqueta="UUID de la factura que la sustituye"
                    value={sustituye}
                    onChange={(e) => setSustituye(e.target.value.trim())}
                    ayuda="El motivo 01 exige relacionar la factura sustituta."
                  />
                )}
              </div>
            </Dialogo>
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
