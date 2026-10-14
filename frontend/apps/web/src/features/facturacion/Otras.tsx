import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import {
  mensajeError,
  useCrearFacturaGlobal,
  useGuardarEmisor,
  useListarSolicitudesAutofactura,
  useObtenerEmisor,
  usePrevisualizarFacturaGlobal,
  type EmisorDto,
  type EstadoSolicitud,
  type SolicitudDto,
  type VentaGlobalDto,
} from '@tg/api-client';
import { REGIMENES_FISCALES, codigoPostal, fechaOperativa, formatoFecha, formatoFechaHora, formatoMoneda, rfc } from '@tg/shared';
import {
  Aviso,
  BarraAccion,
  Boton,
  Campo,
  DialogoConfirmacion,
  EtiquetaEstado,
  Selector,
  TablaDatos,
  Tarjeta,
  TarjetaIndicador,
  etiquetaEnum,
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { BarraFiltros } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';

// ───────────────────────────── Solicitudes de autofactura ─────────────────────────────

const ESTADOS_SOLICITUD: EstadoSolicitud[] = ['Recibida', 'EnProceso', 'Timbrada', 'Rechazada', 'Error'];

export function Solicitudes() {
  const navegar = useNavigate();
  const [estado, setEstado] = useState('');
  const [buscar, setBuscar] = useState('');
  const [pagina, setPagina] = useState(1);
  const { data, isLoading, isFetching } = useListarSolicitudesAutofactura({
    estado: (estado || undefined) as EstadoSolicitud | undefined,
    buscar: buscar || undefined,
    pagina,
    tamano: 50,
  });
  const columnas = useMemo<ColumnDef<SolicitudDto, unknown>[]>(
    () => [
      { header: 'Recibida', accessorKey: 'creadoEn', cell: ({ row }) => formatoFechaHora(row.original.creadoEn) },
      { header: 'Ticket', accessorKey: 'folioTicket', cell: ({ row }) => <span className="font-medium">{row.original.folioTicket}</span> },
      {
        header: 'Receptor',
        accessorKey: 'razonSocial',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.razonSocial}</span>
            <span className="text-xs text-muted">
              {row.original.rfc} · {row.original.email}
            </span>
          </span>
        ),
      },
      {
        header: 'Factura',
        id: 'factura',
        cell: ({ row }) =>
          row.original.factura ? `${row.original.factura.serie}-${row.original.factura.folio} · ${formatoMoneda(row.original.factura.total)}` : '—',
        meta: { ocultarEnMovil: true },
      },
      {
        header: 'Estado',
        accessorKey: 'estado',
        cell: ({ row }) => (
          <span className="flex flex-col items-start gap-1">
            <EtiquetaEstado estado={row.original.estado} />
            {row.original.motivoRechazo && <span className="text-xs text-[var(--tg-status-red-fg)]">{row.original.motivoRechazo}</span>}
          </span>
        ),
      },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina titulo="Autofactura" subtitulo="Solicitudes recibidas en el portal público con el folio y total del ticket." />
      <BarraFiltros>
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={ESTADOS_SOLICITUD.map((e) => ({ valor: e, etiqueta: etiquetaEnum(e) }))}
          value={estado}
          onChange={(e) => (setEstado(e.target.value), setPagina(1))}
        />
        <Campo etiqueta="Buscar ticket, RFC o correo" type="search" value={buscar} onChange={(e) => (setBuscar(e.target.value), setPagina(1))} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(s) => s.id}
        mensajeVacio="No hay solicitudes de autofactura."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
      {data?.items.some((s) => s.factura) && (
        <p className="mt-2 text-sm text-muted">
          Las facturas generadas aparecen en{' '}
          <button type="button" className="text-primary underline" onClick={() => navegar({ to: '/facturacion/facturas' })}>
            Facturas
          </button>
          .
        </p>
      )}
    </Pagina>
  );
}

// ───────────────────────────── Factura global ─────────────────────────────

/** Primer día del mes de una fecha `yyyy-MM-dd`. */
export function inicioDeMes(fecha: string): string {
  return `${fecha.slice(0, 8)}01`;
}

export function FacturaGlobal() {
  const navegar = useNavigate();
  const hoy = fechaOperativa();
  const [desde, setDesde] = useState(inicioDeMes(hoy));
  const [hasta, setHasta] = useState(hoy);
  const [consultado, setConsultado] = useState<{ desde: string; hasta: string } | null>(null);
  const [confirmar, setConfirmar] = useState(false);
  const previa = usePrevisualizarFacturaGlobal(consultado ?? { desde, hasta }, { query: { enabled: !!consultado } });
  const crear = useCrearFacturaGlobal();
  const p = previa.data;
  const columnas = useMemo<ColumnDef<VentaGlobalDto, unknown>[]>(
    () => [
      { header: 'Ticket', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Forma de pago', accessorKey: 'formaPago', cell: ({ row }) => etiquetaEnum(row.original.formaPago), meta: { ocultarEnMovil: true } },
      { header: 'Subtotal', accessorKey: 'subtotal', cell: ({ row }) => formatoMoneda(row.original.subtotal), meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'IVA', accessorKey: 'iva', cell: ({ row }) => formatoMoneda(row.original.iva), meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Total', accessorKey: 'total', cell: ({ row }) => formatoMoneda(row.original.total), meta: { alinear: 'fin' } },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Factura global"
        subtitulo="Ventas de contado sin factura del periodo a PÚBLICO EN GENERAL (XAXX010101000), un concepto por ticket [SUP-11]."
      />
      <div className="flex flex-col gap-4">
        <Tarjeta>
          <div className="flex flex-wrap items-start gap-3">
            <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => setDesde(e.target.value)} className="w-48" />
            <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} className="w-48" />
            <Boton icono="preview" className="mt-1.5" disabled={!desde || !hasta} onClick={() => setConsultado({ desde, hasta })}>
              Previsualizar
            </Boton>
          </div>
        </Tarjeta>
        {previa.error ? <Aviso tipo="error">{mensajeError(previa.error)}</Aviso> : null}
        {p && (
          <>
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
              <TarjetaIndicador icono="receipt" valor={p.ventas} titulo="Tickets" nota={`${formatoFecha(p.desde)} – ${formatoFecha(p.hasta)}`} />
              <TarjetaIndicador icono="calculate" valor={formatoMoneda(p.subtotal)} titulo="Subtotal" />
              <TarjetaIndicador icono="percent" valor={formatoMoneda(p.iva + p.ieps)} titulo="Impuestos" nota={`IVA ${formatoMoneda(p.iva)} · IEPS ${formatoMoneda(p.ieps)}`} />
              <TarjetaIndicador icono="payments" valor={formatoMoneda(p.total)} titulo="Total" nota={`Periodicidad ${p.periodicidad} · ${p.meses}/${p.anio}`} />
            </div>
            <TablaDatos columnas={columnas} datos={p.detalle} cargando={previa.isFetching} idFila={(v) => v.ventaId} mensajeVacio="No hay ventas de contado sin facturar en el periodo." />
            <BarraAccion resumen={`${p.ventas} ticket(s) · ${formatoMoneda(p.total)}`}>
              <Boton disabled={p.ventas === 0} onClick={() => setConfirmar(true)}>
                Generar factura global
              </Boton>
            </BarraAccion>
            <DialogoConfirmacion
              abierto={confirmar}
              onCambiar={setConfirmar}
              titulo="Generar factura global"
              mensaje={`Se factura${p.ventas === 1 ? '' : 'n'} ${p.ventas} ticket(s) por ${formatoMoneda(p.total)}. Esos tickets ya no se podrán autofacturar (RN-FAC-05).`}
              textoConfirmar="Generar"
              alConfirmar={async () => {
                const f = await crear.mutateAsync({ data: { desde: p.desde, hasta: p.hasta } });
                toast.success(`Factura global ${f.serie}-${f.folio} creada; se timbra en segundo plano.`);
                await navegar({ to: '/facturacion/facturas/$id', params: { id: f.id } });
              }}
            />
          </>
        )}
      </div>
    </Pagina>
  );
}

// ───────────────────────────── Emisor ─────────────────────────────

const esquemaEmisor = z.object({
  rfc,
  razonSocial: z.string().trim().min(1, 'Captura la razón social.'),
  regimenFiscal: z.string().min(1, 'Elige el régimen.'),
  codigoPostalExpedicion: codigoPostal,
  serieFacturas: z.string().trim().min(1, 'Captura la serie.').max(10),
  serieGlobal: z.string().trim().min(1, 'Captura la serie.').max(10),
  serieNotasCredito: z.string().trim().min(1, 'Captura la serie.').max(10),
});
type DatosEmisor = z.output<typeof esquemaEmisor>;

export function Emisor() {
  const consulta = useObtenerEmisor();
  return (
    <Pagina ancho="medio">
      <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
        {consulta.data && <FormularioEmisor emisor={consulta.data} />}
      </EstadoConsulta>
    </Pagina>
  );
}

function FormularioEmisor({ emisor }: { emisor: EmisorDto }) {
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('facturacion.configurar');
  const guardar = useGuardarEmisor();
  const [error, setError] = useState<string | null>(null);
  const { register, handleSubmit, formState } = useForm<z.input<typeof esquemaEmisor>, unknown, DatosEmisor>({
    resolver: zodResolver(esquemaEmisor),
    defaultValues: { ...emisor },
  });
  const enviar = handleSubmit(async (d) => {
    setError(null);
    try {
      await guardar.mutateAsync({ data: d });
      toast.success('Datos del emisor guardados.');
    } catch (e) {
      setError(mensajeError(e));
    }
  });
  const e = formState.errors;
  return (
    <>
      <EncabezadoPagina titulo="Emisor" subtitulo="Datos fiscales de la empresa y series de los comprobantes." />
      <form onSubmit={enviar} noValidate className="flex flex-col gap-4">
        {emisor.esEjemplo && (
          <Aviso tipo="advertencia" titulo="Emisor de ejemplo">
            Son datos de prueba del SAT. Captura los reales de la empresa antes de facturar (D-06 pendiente).
          </Aviso>
        )}
        {error && <Aviso tipo="error">{error}</Aviso>}
        <fieldset disabled={!editable} className="m-0 flex flex-col gap-4 border-0 p-0">
          <Tarjeta titulo="Datos fiscales">
            <div className="grid gap-4 sm:grid-cols-2">
              <Campo etiqueta="RFC" error={e.rfc?.message} {...register('rfc')} />
              <Campo etiqueta="Código postal de expedición" inputMode="numeric" error={e.codigoPostalExpedicion?.message} {...register('codigoPostalExpedicion')} />
              <Campo etiqueta="Razón social" className="sm:col-span-2" error={e.razonSocial?.message} {...register('razonSocial')} />
              <Selector
                etiqueta="Régimen fiscal"
                className="sm:col-span-2"
                vacio="Elige el régimen"
                opciones={REGIMENES_FISCALES.map((r) => ({ valor: r.clave, etiqueta: `${r.clave} · ${r.nombre}` }))}
                error={e.regimenFiscal?.message}
                {...register('regimenFiscal')}
              />
            </div>
          </Tarjeta>
          <Tarjeta titulo="Series">
            <div className="grid gap-4 sm:grid-cols-3">
              <Campo etiqueta="Facturas" error={e.serieFacturas?.message} {...register('serieFacturas')} />
              <Campo etiqueta="Factura global" error={e.serieGlobal?.message} {...register('serieGlobal')} />
              <Campo etiqueta="Notas de crédito" error={e.serieNotasCredito?.message} {...register('serieNotasCredito')} />
            </div>
          </Tarjeta>
        </fieldset>
        {editable && (
          <BarraAccion>
            <Boton type="submit" cargando={formState.isSubmitting}>
              Guardar
            </Boton>
          </BarraAccion>
        )}
      </form>
    </>
  );
}
