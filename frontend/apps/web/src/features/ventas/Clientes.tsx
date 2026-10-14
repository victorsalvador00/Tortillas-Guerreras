import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate, useParams } from '@tanstack/react-router';
import {
  esErrorApi,
  mensajeError,
  useActualizarCliente,
  useAprobarUbicacionCliente,
  useCrearCliente,
  useListarCanales,
  useListarClientes,
  useListarListasPrecio,
  useObtenerCliente,
  useObtenerSaldoCliente,
  type ClienteDto,
  type GuardarClienteRequest,
} from '@tg/api-client';
import { REGIMENES_FISCALES, USOS_CFDI, codigoPostal, correo, formatoMoneda, rfc } from '@tg/shared';
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
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useMemo, useState } from 'react';
import { useForm, type Path } from 'react-hook-form';
import { z } from 'zod';
import { BarraFiltros, exportarCsv } from '@/componentes/comunes';
import { MapaLeaflet, type MarcadorMapa } from '@/componentes/MapaLeaflet';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';

const CONDICIONES = [
  { valor: 'Contado', etiqueta: 'Contado' },
  { valor: 'Credito', etiqueta: 'Crédito' },
];

export function Clientes() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const [buscar, setBuscar] = useState('');
  const [canalId, setCanalId] = useState('');
  const [ubicacion, setUbicacion] = useState('');
  const [activo, setActivo] = useState('true');
  const [pagina, setPagina] = useState(1);
  const { data: canales } = useListarCanales();
  const { data, isLoading, isFetching } = useListarClientes({
    buscar: buscar || undefined,
    canalId: canalId || undefined,
    ubicacionPorAprobar: ubicacion === 'porAprobar' ? true : undefined,
    activo: activo === '' ? undefined : activo === 'true',
    pagina,
    tamano: 50,
  });

  const columnas = useMemo<ColumnDef<ClienteDto, unknown>[]>(
    () => [
      { header: 'Código', accessorKey: 'codigo', cell: ({ row }) => <span className="font-medium">{row.original.codigo}</span> },
      {
        header: 'Nombre',
        accessorKey: 'nombre',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.nombre}</span>
            {row.original.razonSocial && <span className="text-xs text-muted">{row.original.razonSocial}</span>}
          </span>
        ),
      },
      { header: 'Canal', accessorKey: 'canal', meta: { ocultarEnMovil: true } },
      { header: 'Lista de precio', accessorKey: 'listaPrecio', meta: { ocultarEnMovil: true } },
      {
        header: 'Pago',
        accessorKey: 'condicionPago',
        cell: ({ row }) =>
          row.original.condicionPago === 'Credito' ? `Crédito ${row.original.diasCredito} días` : 'Contado',
        meta: { ocultarEnMovil: true },
      },
      {
        header: 'Ubicación',
        id: 'ubicacion',
        cell: ({ row }) =>
          row.original.latitudPropuesta != null && !row.original.ubicacionAprobada ? (
            <EtiquetaEstado etiqueta="Por aprobar" color="amarillo" />
          ) : row.original.ubicacionAprobada ? (
            <EtiquetaEstado etiqueta="Aprobada" color="verde" />
          ) : (
            <span className="text-muted">Sin ubicación</span>
          ),
      },
      {
        header: 'Estado',
        accessorKey: 'activo',
        cell: ({ row }) => (
          <span className="flex flex-wrap gap-1">
            <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} />
            {row.original.esGenerico && <EtiquetaEstado etiqueta="Genérico" color="gris" />}
          </span>
        ),
      },
    ],
    [],
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Clientes"
        subtitulo="Institucional, autoservicio y tradicional con sus datos fiscales y ubicación."
        acciones={
          <>
            <Boton
              variante="secundario"
              icono="download"
              disabled={!data?.items.length}
              onClick={() =>
                exportarCsv('clientes', data?.items ?? [], [
                  { titulo: 'Código', valor: (c) => c.codigo },
                  { titulo: 'Nombre', valor: (c) => c.nombre },
                  { titulo: 'Canal', valor: (c) => c.canal },
                  { titulo: 'Lista', valor: (c) => c.listaPrecio },
                  { titulo: 'Condición', valor: (c) => c.condicionPago },
                  { titulo: 'RFC', valor: (c) => c.rfc ?? '' },
                  { titulo: 'Teléfono', valor: (c) => c.telefono ?? '' },
                  { titulo: 'Latitud', valor: (c) => c.latitud ?? '' },
                  { titulo: 'Longitud', valor: (c) => c.longitud ?? '' },
                ])
              }
            >
              Exportar CSV
            </Boton>
            {tienePermiso('ventas.clientes') && (
              <Boton icono="add" onClick={() => navegar({ to: '/ventas/clientes/nuevo' })}>
                Nuevo cliente
              </Boton>
            )}
          </>
        }
      />
      <BarraFiltros>
        <Campo
          etiqueta="Buscar por código, nombre o RFC"
          type="search"
          value={buscar}
          onChange={(e) => {
            setBuscar(e.target.value);
            setPagina(1);
          }}
        />
        <Selector
          etiqueta="Canal"
          vacio="Todos"
          opciones={(canales ?? []).map((c) => ({ valor: c.id, etiqueta: c.nombre }))}
          value={canalId}
          onChange={(e) => {
            setCanalId(e.target.value);
            setPagina(1);
          }}
        />
        <Selector
          etiqueta="Ubicación"
          vacio="Todas"
          opciones={[{ valor: 'porAprobar', etiqueta: 'Por aprobar' }]}
          value={ubicacion}
          onChange={(e) => {
            setUbicacion(e.target.value);
            setPagina(1);
          }}
        />
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={[
            { valor: 'true', etiqueta: 'Activos' },
            { valor: 'false', etiqueta: 'Inactivos' },
          ]}
          value={activo}
          onChange={(e) => {
            setActivo(e.target.value);
            setPagina(1);
          }}
        />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(c) => c.id}
        alClicFila={(c) => navegar({ to: '/ventas/clientes/$id', params: { id: c.id } })}
        mensajeVacio="No hay clientes con esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </Pagina>
  );
}

const textoOpcional = z.string().trim().optional();
const numeroOpcional = z.preprocess((v) => (v === '' || v == null ? null : Number(v)), z.number().nullable());

const esquema = z
  .object({
    codigo: textoOpcional,
    nombre: z.string().trim().min(1, 'Captura el nombre comercial.'),
    canalId: z.string().min(1, 'Elige el canal.'),
    listaPrecioId: z.string().min(1, 'Elige la lista de precios.'),
    condicionPago: z.enum(['Contado', 'Credito']),
    diasCredito: z.coerce.number().int().min(0, 'No puede ser negativo.'),
    limiteCredito: numeroOpcional,
    requiereFactura: z.boolean(),
    rfc: textoOpcional,
    razonSocial: textoOpcional,
    regimenFiscal: textoOpcional,
    codigoPostalFiscal: textoOpcional,
    usoCfdi: textoOpcional,
    emailFacturacion: textoOpcional,
    telefono: textoOpcional,
    calle: textoOpcional,
    colonia: textoOpcional,
    municipio: textoOpcional,
    estado: textoOpcional,
    codigoPostal: textoOpcional,
    latitud: numeroOpcional,
    longitud: numeroOpcional,
    activo: z.boolean(),
  })
  .superRefine((d, ctx) => {
    const error = (path: string, message: string) => ctx.addIssue({ code: 'custom', path: [path], message });
    if (d.rfc && !rfc.safeParse(d.rfc).success) error('rfc', 'El RFC no tiene un formato válido.');
    if (d.codigoPostalFiscal && !codigoPostal.safeParse(d.codigoPostalFiscal).success) error('codigoPostalFiscal', 'El código postal tiene 5 dígitos.');
    if (d.codigoPostal && !codigoPostal.safeParse(d.codigoPostal).success) error('codigoPostal', 'El código postal tiene 5 dígitos.');
    if (d.emailFacturacion && !correo.safeParse(d.emailFacturacion).success) error('emailFacturacion', 'El correo no es válido.');
    if (d.requiereFactura) {
      if (!d.rfc) error('rfc', 'Captura el RFC para facturar.');
      if (!d.razonSocial) error('razonSocial', 'Captura la razón social.');
      if (!d.regimenFiscal) error('regimenFiscal', 'Elige el régimen fiscal.');
      if (!d.codigoPostalFiscal) error('codigoPostalFiscal', 'Captura el código postal fiscal.');
      if (!d.usoCfdi) error('usoCfdi', 'Elige el uso de CFDI.');
    }
    if ((d.latitud == null) !== (d.longitud == null)) error('longitud', 'Captura latitud y longitud.');
    if (d.latitud != null && (d.latitud < -90 || d.latitud > 90)) error('latitud', 'Latitud entre -90 y 90.');
    if (d.longitud != null && (d.longitud < -180 || d.longitud > 180)) error('longitud', 'Longitud entre -180 y 180.');
  });

type Entrada = z.input<typeof esquema>;
type Datos = z.output<typeof esquema>;

function aFormulario(c?: ClienteDto): Entrada {
  return {
    codigo: c?.codigo ?? '',
    nombre: c?.nombre ?? '',
    canalId: c?.canalId ?? '',
    listaPrecioId: c?.listaPrecioId ?? '',
    condicionPago: c?.condicionPago ?? 'Contado',
    diasCredito: c?.diasCredito ?? 0,
    limiteCredito: c?.limiteCredito ?? '',
    requiereFactura: c?.requiereFactura ?? false,
    rfc: c?.rfc ?? '',
    razonSocial: c?.razonSocial ?? '',
    regimenFiscal: c?.regimenFiscal ?? '',
    codigoPostalFiscal: c?.codigoPostalFiscal ?? '',
    usoCfdi: c?.usoCfdi ?? '',
    emailFacturacion: c?.emailFacturacion ?? '',
    telefono: c?.telefono ?? '',
    calle: c?.calle ?? '',
    colonia: c?.colonia ?? '',
    municipio: c?.municipio ?? '',
    estado: c?.estado ?? '',
    codigoPostal: c?.codigoPostal ?? '',
    latitud: c?.latitud ?? '',
    longitud: c?.longitud ?? '',
    activo: c?.activo ?? true,
  };
}

const vacioANulo = (v: string | undefined) => (v ? v : null);

function aSolicitud(d: Datos): GuardarClienteRequest {
  return {
    codigo: vacioANulo(d.codigo),
    nombre: d.nombre,
    canalId: d.canalId,
    listaPrecioId: d.listaPrecioId,
    condicionPago: d.condicionPago,
    diasCredito: d.condicionPago === 'Credito' ? d.diasCredito : 0,
    limiteCredito: d.condicionPago === 'Credito' ? d.limiteCredito : null,
    requiereFactura: d.requiereFactura,
    rfc: d.rfc ? d.rfc.toUpperCase() : null,
    razonSocial: vacioANulo(d.razonSocial),
    regimenFiscal: vacioANulo(d.regimenFiscal),
    codigoPostalFiscal: vacioANulo(d.codigoPostalFiscal),
    usoCfdi: vacioANulo(d.usoCfdi),
    emailFacturacion: vacioANulo(d.emailFacturacion),
    telefono: vacioANulo(d.telefono),
    calle: vacioANulo(d.calle),
    colonia: vacioANulo(d.colonia),
    municipio: vacioANulo(d.municipio),
    estado: vacioANulo(d.estado),
    codigoPostal: vacioANulo(d.codigoPostal),
    latitud: d.latitud,
    longitud: d.longitud,
    activo: d.activo,
  };
}

export function ClienteFormulario() {
  const { id } = useParams({ strict: false }) as { id?: string };
  const nuevo = !id;
  const consulta = useObtenerCliente(id ?? '', { query: { enabled: !nuevo } });
  const canales = useListarCanales();
  const listas = useListarListasPrecio();
  return (
    <Pagina ancho="medio">
      {/* Las listas nativas necesitan sus opciones antes de recibir el valor inicial. */}
      <EstadoConsulta
        cargando={(!nuevo && consulta.isLoading) || canales.isLoading || listas.isLoading}
        error={consulta.error ?? canales.error ?? listas.error}
      >
        {/* La llave cambia al aprobar la ubicación para recargar las coordenadas del formulario. */}
        <FormularioCliente
          key={consulta.data ? `${consulta.data.id}-${consulta.data.latitud}-${consulta.data.latitudPropuesta}` : 'nuevo'}
          cliente={consulta.data}
        />
      </EstadoConsulta>
    </Pagina>
  );
}

function FormularioCliente({ cliente }: { cliente?: ClienteDto }) {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('ventas.clientes');
  const supervisa = tienePermiso('ventas.supervisar');
  const { data: canales } = useListarCanales();
  const { data: listas } = useListarListasPrecio();
  const crear = useCrearCliente();
  const actualizar = useActualizarCliente();
  const aprobar = useAprobarUbicacionCliente();
  const saldo = useObtenerSaldoCliente(cliente?.id ?? '', { query: { enabled: cliente?.condicionPago === 'Credito' } });
  const [errorGeneral, setErrorGeneral] = useState<string | null>(null);
  const [confirmarUbicacion, setConfirmarUbicacion] = useState(false);

  const {
    register,
    handleSubmit,
    watch,
    setValue,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<Entrada, unknown, Datos>({ resolver: zodResolver(esquema), defaultValues: aFormulario(cliente) });
  const condicion = watch('condicionPago');
  const requiereFactura = watch('requiereFactura');
  const lat = watch('latitud');
  const lng = watch('longitud');
  const canalId = watch('canalId');

  const marcadores: MarcadorMapa[] = [];
  const latNum = lat === '' || lat == null ? null : Number(lat);
  const lngNum = lng === '' || lng == null ? null : Number(lng);
  if (latNum != null && lngNum != null && Number.isFinite(latNum) && Number.isFinite(lngNum)) {
    marcadores.push({
      id: 'registrada',
      lat: latNum,
      lng: lngNum,
      titulo: cliente?.ubicacionAprobada ? 'Ubicación aprobada' : 'Ubicación registrada',
      color: 'azul',
    });
  }
  const hayPropuesta = cliente?.latitudPropuesta != null && cliente?.longitudPropuesta != null;
  if (hayPropuesta) {
    marcadores.push({
      id: 'propuesta',
      lat: cliente!.latitudPropuesta!,
      lng: cliente!.longitudPropuesta!,
      titulo: 'Ubicación propuesta en ruta',
      detalle: 'Capturada por el vendedor; por aprobar (RN-VEN-06).',
      color: 'naranja',
    });
  }

  const enviar = handleSubmit(async (d) => {
    setErrorGeneral(null);
    try {
      const datos = aSolicitud(d);
      const guardado = cliente
        ? await actualizar.mutateAsync({ id: cliente.id, data: datos })
        : await crear.mutateAsync({ data: datos });
      toast.success(cliente ? 'Cliente actualizado.' : 'Cliente creado.');
      if (!cliente) await navegar({ to: '/ventas/clientes/$id', params: { id: guardado.id } });
    } catch (e) {
      if (esErrorApi(e)) {
        Object.entries(e.errores).forEach(([campo, m]) => setError(campo as Path<Entrada>, { message: m[0] }));
      }
      setErrorGeneral(mensajeError(e));
    }
  });

  return (
    <>
      <EncabezadoPagina
        titulo={cliente ? cliente.nombre : 'Nuevo cliente'}
        subtitulo={cliente ? `${cliente.codigo} · ${cliente.canal}${cliente.esGenerico ? ' · Público en general' : ''}` : 'El código se asigna solo si lo dejas vacío.'}
        migas={[{ etiqueta: 'Ventas' }, { etiqueta: 'Clientes', a: '/ventas/clientes' }, { etiqueta: cliente ? cliente.codigo : 'Nuevo' }]}
      />
      <form onSubmit={enviar} noValidate className="flex flex-col gap-4">
        {errorGeneral && <Aviso tipo="error">{errorGeneral}</Aviso>}
        {cliente?.condicionPago === 'Credito' && saldo.data && (
          <Tarjeta titulo="Crédito" subtitulo="Saldo de ventas a crédito menos cobros [SUP-08].">
            <Datos>
              <Dato etiqueta="Saldo">{formatoMoneda(saldo.data.saldo)}</Dato>
              <Dato etiqueta="Límite">{saldo.data.limiteCredito != null ? formatoMoneda(saldo.data.limiteCredito) : 'Sin límite'}</Dato>
              <Dato etiqueta="Disponible">{saldo.data.disponible != null ? formatoMoneda(saldo.data.disponible) : '—'}</Dato>
            </Datos>
          </Tarjeta>
        )}
        <fieldset disabled={!editable} className="m-0 flex min-w-0 flex-col gap-4 border-0 p-0">
          <Tarjeta titulo="Datos generales">
            <div className="grid gap-4 sm:grid-cols-2">
              <Campo etiqueta="Nombre comercial" error={errors.nombre?.message} {...register('nombre')} />
              <Campo etiqueta="Código (opcional)" error={errors.codigo?.message} {...register('codigo')} />
              <Selector
                etiqueta="Canal"
                vacio="Elige el canal"
                opciones={(canales ?? []).filter((c) => c.activo || c.id === canalId).map((c) => ({ valor: c.id, etiqueta: `${c.clave} · ${c.nombre}` }))}
                error={errors.canalId?.message}
                {...register('canalId', {
                  onChange: (e) => {
                    const canal = canales?.find((c) => c.id === e.target.value);
                    const lista = listas?.find((l) => l.canalId === e.target.value && l.activo);
                    if (lista && !cliente) setValue('listaPrecioId', lista.id);
                    if (canal && !cliente) setValue('requiereFactura', canal.requiereFacturaDefault);
                  },
                })}
              />
              <Selector
                etiqueta="Lista de precios"
                vacio="Elige la lista"
                opciones={(listas ?? []).map((l) => ({ valor: l.id, etiqueta: l.nombre }))}
                error={errors.listaPrecioId?.message}
                {...register('listaPrecioId')}
              />
              <Campo etiqueta="Teléfono" type="tel" {...register('telefono')} />
              <div className="flex items-center">
                <Casilla etiqueta="Activo" {...register('activo')} />
              </div>
            </div>
          </Tarjeta>

          <Tarjeta titulo="Condiciones de pago" subtitulo="Crédito con límite y cobranza en ruta [SUP-08].">
            <div className="grid gap-4 sm:grid-cols-3">
              <Selector etiqueta="Condición" opciones={CONDICIONES} {...register('condicionPago')} />
              <Campo
                etiqueta="Días de crédito"
                type="number"
                inputMode="numeric"
                disabled={condicion !== 'Credito'}
                error={errors.diasCredito?.message}
                {...register('diasCredito')}
              />
              <Campo
                etiqueta="Límite de crédito"
                type="number"
                step="any"
                prefijo="$"
                disabled={condicion !== 'Credito'}
                ayuda="Vacío = sin límite."
                error={errors.limiteCredito?.message}
                {...register('limiteCredito')}
              />
            </div>
          </Tarjeta>

          <Tarjeta titulo="Datos fiscales (CFDI 4.0)" subtitulo="Obligatorios si el cliente requiere factura.">
            <Casilla etiqueta="Requiere factura" className="mb-3" {...register('requiereFactura')} />
            <div className="grid gap-4 sm:grid-cols-2">
              <Campo etiqueta={`RFC${requiereFactura ? '' : ' (opcional)'}`} autoCapitalize="characters" error={errors.rfc?.message} {...register('rfc')} />
              <Campo etiqueta="Razón social" error={errors.razonSocial?.message} {...register('razonSocial')} />
              <Selector
                etiqueta="Régimen fiscal"
                vacio="Elige"
                opciones={REGIMENES_FISCALES.map((r) => ({ valor: r.clave, etiqueta: `${r.clave} · ${r.nombre}` }))}
                error={errors.regimenFiscal?.message}
                {...register('regimenFiscal')}
              />
              <Selector
                etiqueta="Uso de CFDI"
                vacio="Elige"
                opciones={USOS_CFDI.map((u) => ({ valor: u.clave, etiqueta: `${u.clave} · ${u.nombre}` }))}
                error={errors.usoCfdi?.message}
                {...register('usoCfdi')}
              />
              <Campo etiqueta="Código postal fiscal" inputMode="numeric" error={errors.codigoPostalFiscal?.message} {...register('codigoPostalFiscal')} />
              <Campo etiqueta="Correo para facturas" type="email" error={errors.emailFacturacion?.message} {...register('emailFacturacion')} />
            </div>
          </Tarjeta>

          <Tarjeta titulo="Dirección">
            <div className="grid gap-4 sm:grid-cols-2">
              <Campo etiqueta="Calle y número" className="sm:col-span-2" {...register('calle')} />
              <Campo etiqueta="Colonia" {...register('colonia')} />
              <Campo etiqueta="Municipio" {...register('municipio')} />
              <Campo etiqueta="Estado" {...register('estado')} />
              <Campo etiqueta="Código postal" inputMode="numeric" error={errors.codigoPostal?.message} {...register('codigoPostal')} />
            </div>
          </Tarjeta>
        </fieldset>

        <Tarjeta
          titulo="Ubicación"
          subtitulo={editable ? 'Haz clic en el mapa para fijar la ubicación o captura las coordenadas.' : undefined}
          acciones={
            hayPropuesta &&
            supervisa &&
            cliente && (
              <Boton variante="secundario" tamano="chico" icono="where_to_vote" onClick={() => setConfirmarUbicacion(true)}>
                Aprobar ubicación propuesta
              </Boton>
            )
          }
        >
          {hayPropuesta && (
            <Aviso tipo="advertencia" className="mb-3">
              El vendedor propuso una ubicación desde la ruta (punto naranja). Revísala y apruébala.
            </Aviso>
          )}
          <MapaLeaflet
            marcadores={marcadores}
            alClicMapa={
              editable
                ? (la, ln) => {
                    setValue('latitud', Number(la.toFixed(6)), { shouldDirty: true });
                    setValue('longitud', Number(ln.toFixed(6)), { shouldDirty: true });
                  }
                : undefined
            }
          />
          <div className="mt-4 grid gap-4 sm:grid-cols-2">
            <Campo etiqueta="Latitud" type="number" step="any" disabled={!editable} error={errors.latitud?.message} {...register('latitud')} />
            <Campo etiqueta="Longitud" type="number" step="any" disabled={!editable} error={errors.longitud?.message} {...register('longitud')} />
          </div>
          {cliente && (
            <p className="m-0 mt-2 text-sm text-muted">
              {cliente.ubicacionAprobada ? 'Ubicación aprobada: se usa para validar el GPS de las ventas (RN-VEN-05).' : 'Sin ubicación aprobada.'}
            </p>
          )}
        </Tarjeta>

        {editable && (
          <BarraAccion resumen={cliente ? 'Los cambios bajan a la app de ruta en la siguiente sincronización.' : undefined}>
            <Boton variante="texto" onClick={() => navegar({ to: '/ventas/clientes' })}>
              Cancelar
            </Boton>
            <Boton type="submit" cargando={isSubmitting}>
              {cliente ? 'Guardar cambios' : 'Crear cliente'}
            </Boton>
          </BarraAccion>
        )}
      </form>

      {cliente && hayPropuesta && (
        <DialogoConfirmacion
          abierto={confirmarUbicacion}
          onCambiar={setConfirmarUbicacion}
          titulo="Aprobar ubicación"
          mensaje={`La ubicación propuesta (${cliente.latitudPropuesta}, ${cliente.longitudPropuesta}) será la oficial de ${cliente.nombre}.`}
          textoConfirmar="Aprobar"
          alConfirmar={async () => {
            await aprobar.mutateAsync({ id: cliente.id, data: {} });
            toast.success('Ubicación aprobada.');
          }}
        />
      )}
    </>
  );
}
