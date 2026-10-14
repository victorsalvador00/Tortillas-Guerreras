import { zodResolver } from '@hookform/resolvers/zod';
import {
  captchaPortal,
  clienteApi,
  consultarTicketPortal,
  esErrorApi,
  mensajeError,
  obtenerSolicitudPortal,
  solicitarAutofactura,
  type CaptchaDto,
  type SolicitudDto,
  type TicketPortalDto,
} from '@tg/api-client';
import { REGIMENES_FISCALES, USOS_CFDI, codigoPostal, correo, formatoFecha, formatoMoneda, rfc } from '@tg/shared';
import { Aviso, Boton, BotonIcono, Campo, EtiquetaEstado, Girador, Icono, Logo, MenuAcciones, OPCIONES_TEMA, Selector, Tarjeta, useTema } from '@tg/ui';
import { useCallback, useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

/** El QR del ticket apunta a /f/<folio> (07-web). */
function folioDeUrl(): string {
  const m = window.location.pathname.match(/^\/f\/([^/]+)/);
  return m ? decodeURIComponent(m[1]) : '';
}

function solicitudDeUrl(): string | null {
  return new URLSearchParams(window.location.search).get('solicitud');
}

async function descargar(url: string, nombre: string) {
  const blob = await clienteApi<Blob>({ url, method: 'GET', responseType: 'blob' });
  const enlace = document.createElement('a');
  enlace.href = URL.createObjectURL(blob);
  enlace.download = nombre;
  enlace.click();
  URL.revokeObjectURL(enlace.href);
}

const PASOS = ['Ticket', 'Datos fiscales', 'Factura'];

function Pasos({ actual }: { actual: number }) {
  return (
    <ol className="m-0 mb-6 flex list-none items-center gap-2 p-0 text-sm">
      {PASOS.map((p, i) => (
        <li key={p} className="flex items-center gap-2">
          <span
            className={`grid size-7 place-items-center rounded-full text-xs font-semibold ${
              i < actual ? 'bg-[var(--tg-status-green-bg)] text-[var(--tg-status-green-fg)]' : i === actual ? 'bg-primary text-on-primary' : 'bg-soft text-muted'
            }`}
            aria-current={i === actual ? 'step' : undefined}
          >
            {i < actual ? <Icono nombre="check" tamano={16} /> : i + 1}
          </span>
          <span className={i === actual ? 'font-medium' : 'text-muted'}>{p}</span>
          {i < PASOS.length - 1 && <span className="mx-1 h-px w-6 bg-outline-variant" aria-hidden />}
        </li>
      ))}
    </ol>
  );
}

// ───────────────────────────── Paso 1: ticket ─────────────────────────────

const esquemaTicket = z.object({
  folio: z.string().trim().min(1, 'Escribe el folio de tu ticket.'),
  total: z.coerce.number({ message: 'Escribe el total del ticket.' }).positive('Escribe el total del ticket.'),
});

function PasoTicket({ alEncontrar }: { alEncontrar: (t: TicketPortalDto, total: number) => void }) {
  const [error, setError] = useState<string | null>(null);
  const { register, handleSubmit, formState } = useForm<z.input<typeof esquemaTicket>, unknown, z.output<typeof esquemaTicket>>({
    resolver: zodResolver(esquemaTicket),
    defaultValues: { folio: folioDeUrl(), total: '' as unknown as number },
  });
  const enviar = handleSubmit(async ({ folio, total }) => {
    setError(null);
    try {
      const ticket = await consultarTicketPortal(folio.toUpperCase(), { total });
      alEncontrar(ticket, total);
    } catch (e) {
      setError(esErrorApi(e) && e.estado === 404 ? 'No encontramos un ticket con ese folio y total. Revisa los datos.' : mensajeError(e));
    }
  });
  return (
    <form onSubmit={enviar} noValidate className="grid gap-4">
      <p className="m-0 text-muted">Escribe el folio y el total que vienen en tu ticket de compra.</p>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <Campo etiqueta="Folio del ticket" autoCapitalize="characters" error={formState.errors.folio?.message} ayuda="Ejemplo: R01-D03-000123" {...register('folio')} />
      <Campo etiqueta="Total del ticket" type="number" step="0.01" inputMode="decimal" prefijo="$" error={formState.errors.total?.message} {...register('total')} />
      <div>
        <Boton type="submit" cargando={formState.isSubmitting} iconoFinal="arrow_forward">
          Buscar ticket
        </Boton>
      </div>
    </form>
  );
}

// ───────────────────────────── Paso 2: datos fiscales ─────────────────────────────

const esquemaFiscal = z.object({
  rfc,
  razonSocial: z.string().trim().min(1, 'Escribe tu nombre o razón social como aparece en tu constancia.'),
  regimenFiscal: z.string().min(1, 'Elige tu régimen fiscal.'),
  codigoPostal,
  usoCfdi: z.string().min(1, 'Elige el uso del CFDI.'),
  email: correo,
  captchaRespuesta: z.coerce.number({ message: 'Responde la pregunta.' }),
});

function PasoDatos({ ticket, total, alSolicitar, alVolver }: { ticket: TicketPortalDto; total: number; alSolicitar: (s: SolicitudDto) => void; alVolver: () => void }) {
  const [captcha, setCaptcha] = useState<CaptchaDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const cargarCaptcha = useCallback(() => {
    captchaPortal().then(setCaptcha, () => setCaptcha(null));
  }, []);
  useEffect(cargarCaptcha, [cargarCaptcha]);

  const { register, handleSubmit, setError: marcar, setValue, formState } = useForm<z.input<typeof esquemaFiscal>, unknown, z.output<typeof esquemaFiscal>>({
    resolver: zodResolver(esquemaFiscal),
    defaultValues: { usoCfdi: 'G03', regimenFiscal: '' },
  });

  const enviar = handleSubmit(async (d) => {
    if (!captcha) return;
    setError(null);
    try {
      const solicitud = await solicitarAutofactura({ ...d, folio: ticket.folio, total, captchaId: captcha.id, rfc: d.rfc.toUpperCase() });
      alSolicitar(solicitud);
    } catch (e) {
      if (esErrorApi(e)) {
        Object.entries(e.errores).forEach(([c, m]) => marcar(c as keyof z.input<typeof esquemaFiscal>, { message: m[0] }));
      }
      setError(mensajeError(e));
      setValue('captchaRespuesta', '' as unknown as number);
      cargarCaptcha();
    }
  });

  return (
    <form onSubmit={enviar} noValidate className="grid gap-4">
      <div className="rounded-xl bg-soft px-4 py-3 text-sm">
        Ticket <strong>{ticket.folio}</strong> del {formatoFecha(ticket.fecha)} por <strong>{formatoMoneda(ticket.total)}</strong>
      </div>
      {error && <Aviso tipo="error">{error}</Aviso>}
      <div className="grid gap-4 sm:grid-cols-2">
        <Campo etiqueta="RFC" autoCapitalize="characters" error={formState.errors.rfc?.message} {...register('rfc')} />
        <Campo etiqueta="Código postal fiscal" inputMode="numeric" maxLength={5} error={formState.errors.codigoPostal?.message} {...register('codigoPostal')} />
        <Campo etiqueta="Nombre o razón social" className="sm:col-span-2" ayuda="Sin régimen societario (sin S.A. de C.V.), como en tu constancia." error={formState.errors.razonSocial?.message} {...register('razonSocial')} />
        <Selector
          etiqueta="Régimen fiscal"
          vacio="Elige tu régimen"
          className="sm:col-span-2"
          opciones={REGIMENES_FISCALES.map((r) => ({ valor: r.clave, etiqueta: `${r.clave} · ${r.nombre}` }))}
          error={formState.errors.regimenFiscal?.message}
          {...register('regimenFiscal')}
        />
        <Selector
          etiqueta="Uso del CFDI"
          opciones={USOS_CFDI.map((u) => ({ valor: u.clave, etiqueta: `${u.clave} · ${u.nombre}` }))}
          error={formState.errors.usoCfdi?.message}
          {...register('usoCfdi')}
        />
        <Campo etiqueta="Correo para recibir la factura" type="email" autoComplete="email" error={formState.errors.email?.message} {...register('email')} />
      </div>
      <div className="grid items-start gap-3 sm:grid-cols-[1fr_auto]">
        <Campo
          etiqueta={captcha ? `Para continuar: ¿cuánto es ${captcha.pregunta}?` : 'Cargando verificación…'}
          type="number"
          inputMode="numeric"
          disabled={!captcha}
          error={formState.errors.captchaRespuesta?.message}
          {...register('captchaRespuesta')}
        />
        <BotonIcono icono="refresh" etiqueta="Otra pregunta" className="mt-2" onClick={cargarCaptcha} />
      </div>
      <div className="flex flex-wrap gap-2">
        <Boton variante="texto" icono="arrow_back" onClick={alVolver}>
          Volver
        </Boton>
        <Boton type="submit" cargando={formState.isSubmitting} disabled={!captcha}>
          Solicitar factura
        </Boton>
      </div>
    </form>
  );
}

// ───────────────────────────── Paso 3: estado y descarga ─────────────────────────────

function PasoEstado({ solicitudId, alNueva }: { solicitudId: string; alNueva: () => void }) {
  const [solicitud, setSolicitud] = useState<SolicitudDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [descargando, setDescargando] = useState<string | null>(null);

  useEffect(() => {
    let vigente = true;
    let espera: ReturnType<typeof setTimeout>;
    const consultar = async () => {
      try {
        const s = await obtenerSolicitudPortal(solicitudId);
        if (!vigente) return;
        setSolicitud(s);
        if (s.estado === 'Recibida' || s.estado === 'EnProceso') espera = setTimeout(consultar, 3000);
      } catch (e) {
        if (vigente) setError(mensajeError(e));
      }
    };
    void consultar();
    return () => {
      vigente = false;
      clearTimeout(espera);
    };
  }, [solicitudId]);

  if (error) return <Aviso tipo="error">{error}</Aviso>;
  if (!solicitud) {
    return (
      <p className="flex items-center gap-2 text-muted">
        <Girador /> Consultando tu solicitud…
      </p>
    );
  }

  const nombre = solicitud.factura ? `${solicitud.factura.serie}${solicitud.factura.folio}` : solicitud.folioTicket;
  const bajar = async (tipo: 'xml' | 'pdf') => {
    setDescargando(tipo);
    try {
      await descargar(`/api/v1/portal/solicitudes/${solicitud.id}/${tipo}`, `${nombre}.${tipo}`);
    } catch (e) {
      setError(mensajeError(e));
    } finally {
      setDescargando(null);
    }
  };

  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="m-0 text-sm text-muted">Ticket {solicitud.folioTicket}</p>
          <p className="m-0 text-lg font-medium">
            {solicitud.razonSocial} · {solicitud.rfc}
          </p>
        </div>
        <EtiquetaEstado estado={solicitud.estado} etiqueta={solicitud.estado === 'Timbrada' ? 'Lista' : undefined} />
      </div>

      {(solicitud.estado === 'Recibida' || solicitud.estado === 'EnProceso') && (
        <Aviso tipo="info" titulo="Generando tu factura">
          <span className="inline-flex items-center gap-2">
            <Girador className="size-4" /> Esto toma unos segundos; puedes esperar aquí.
          </span>
        </Aviso>
      )}
      {solicitud.estado === 'Rechazada' && (
        <Aviso tipo="error" titulo="No se pudo facturar el ticket">
          {solicitud.motivoRechazo ?? 'Revisa los datos e intenta de nuevo.'}
        </Aviso>
      )}
      {solicitud.estado === 'Error' && (
        <Aviso tipo="advertencia" titulo="Hubo un problema al timbrar">
          Lo estamos reintentando. Vuelve más tarde con esta misma página o revisa tu correo.
        </Aviso>
      )}
      {solicitud.estado === 'Timbrada' && solicitud.factura && (
        <>
          <Aviso tipo="exito" titulo="Tu factura está lista">
            Folio {nombre} · UUID {solicitud.factura.uuidFiscal} · {formatoMoneda(solicitud.factura.total)}. También la enviamos a{' '}
            {solicitud.email}.
          </Aviso>
          <div className="flex flex-wrap gap-2">
            <Boton icono="picture_as_pdf" cargando={descargando === 'pdf'} onClick={() => bajar('pdf')}>
              Descargar PDF
            </Boton>
            <Boton variante="secundario" icono="code" cargando={descargando === 'xml'} onClick={() => bajar('xml')}>
              Descargar XML
            </Boton>
          </div>
        </>
      )}
      <div>
        <Boton variante="texto" icono="add" onClick={alNueva}>
          Facturar otro ticket
        </Boton>
      </div>
    </div>
  );
}

export function Portal() {
  const { oscuro, modo, fijar } = useTema();
  const [ticket, setTicket] = useState<{ datos: TicketPortalDto; total: number } | null>(null);
  const [solicitudId, setSolicitudId] = useState<string | null>(solicitudDeUrl);
  const paso = solicitudId ? 2 : ticket ? 1 : 0;

  const irASolicitud = (id: string) => {
    setSolicitudId(id);
    window.history.replaceState(null, '', `/?solicitud=${id}`);
  };

  return (
    <div className="flex min-h-full flex-col">
      <header className="flex h-16 shrink-0 items-center gap-3 bg-topbar px-4 text-topbar-ink">
        <span className="grid place-items-center rounded-lg bg-white px-1.5 py-1">
          <Logo chico titulo="Guerreras" />
        </span>
        <span className="text-lg font-semibold">Facturación electrónica</span>
        <span className="flex-1" />
        <MenuAcciones
          disparador={<BotonIcono icono={oscuro ? 'dark_mode' : 'light_mode'} etiqueta="Cambiar tema" plano className="text-topbar-ink" />}
          opciones={OPCIONES_TEMA.map((t) => ({ etiqueta: t.etiqueta, icono: t.icono, marcada: modo === t.modo, alSeleccionar: () => fijar(t.modo) }))}
        />
      </header>
      <main className="mx-auto w-full max-w-2xl flex-1 px-4 py-8">
        <h1 className="m-0 mb-1 text-2xl font-semibold">Factura tu compra</h1>
        <p className="m-0 mb-6 text-muted">Genera tu CFDI 4.0 con el folio de tu ticket. Tienes hasta el último día del mes de la compra.</p>
        <Tarjeta>
          <Pasos actual={paso} />
          {paso === 0 && (
            <PasoTicket
              alEncontrar={(t, total) => {
                if (t.solicitudId) irASolicitud(t.solicitudId);
                else setTicket({ datos: t, total });
              }}
            />
          )}
          {paso === 1 && ticket && !ticket.datos.facturable && (
            <div className="grid gap-4">
              <Aviso tipo="advertencia" titulo="Este ticket no se puede facturar">
                {ticket.datos.motivo ?? 'Comunícate con nosotros para revisarlo.'}
              </Aviso>
              <div>
                <Boton variante="texto" icono="arrow_back" onClick={() => setTicket(null)}>
                  Volver
                </Boton>
              </div>
            </div>
          )}
          {paso === 1 && ticket?.datos.facturable && (
            <PasoDatos ticket={ticket.datos} total={ticket.total} alSolicitar={(s) => irASolicitud(s.id)} alVolver={() => setTicket(null)} />
          )}
          {paso === 2 && solicitudId && (
            <PasoEstado
              solicitudId={solicitudId}
              alNueva={() => {
                setSolicitudId(null);
                setTicket(null);
                window.history.replaceState(null, '', '/');
              }}
            />
          )}
        </Tarjeta>
        <p className="mt-6 text-center text-xs text-muted">Guerreras · Por Familia Ochoa</p>
      </main>
    </div>
  );
}
