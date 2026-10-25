import { zodResolver } from '@hookform/resolvers/zod';
import {
  esErrorApi,
  mensajeError,
  useActivarProveedor,
  useActualizarProveedor,
  useCrearProveedor,
  useDesactivarProveedor,
  useListarPreciosProveedor,
  useListarProveedores,
  type GuardarProveedorRequest,
  type PrecioProveedorDto,
  type ProveedorDto,
} from '@tg/api-client';
import { formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda } from '@tg/shared';
import {
  Aviso,
  Boton,
  BotonIcono,
  Campo,
  Dialogo,
  DialogoConfirmacion,
  EtiquetaEstado,
  MenuAcciones,
  Selector,
  TablaDatos,
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useMemo, useState } from 'react';
import { useForm, type Path } from 'react-hook-form';
import { z } from 'zod';
import { BarraFiltros, exportarCsv } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { DialogoProductosProveedor } from './ProductosProveedor';

const opcional = z.string().trim().optional();

const esquema = z.object({
  codigo: z.string().trim().min(1, 'Captura el código.').max(20),
  razonSocial: z.string().trim().min(1, 'Captura la razón social.'),
  nombreComercial: opcional,
  rfc: z
    .string()
    .trim()
    .toUpperCase()
    .refine((v) => v === '' || /^[A-ZÑ&]{3,4}\d{6}[A-Z\d]{3}$/.test(v), 'El RFC debe tener 12 o 13 caracteres válidos.')
    .optional(),
  contacto: opcional,
  telefono: opcional,
  email: z
    .string()
    .trim()
    .refine((v) => v === '' || z.string().email().safeParse(v).success, 'El correo no es válido.')
    .optional(),
  diasCredito: z.coerce.number().int().min(0, 'No puede ser negativo.'),
});
type Entrada = z.input<typeof esquema>;
type Datos = z.output<typeof esquema>;

const vacioANulo = (v?: string) => (v ? v : null);

function aSolicitud(d: Datos): GuardarProveedorRequest {
  return {
    codigo: d.codigo.toUpperCase(),
    razonSocial: d.razonSocial,
    nombreComercial: vacioANulo(d.nombreComercial),
    rfc: vacioANulo(d.rfc),
    contacto: vacioANulo(d.contacto),
    telefono: vacioANulo(d.telefono),
    email: vacioANulo(d.email),
    diasCredito: d.diasCredito,
  };
}

function DialogoProveedor({ proveedor, abierto, onCambiar }: { proveedor: ProveedorDto | null; abierto: boolean; onCambiar: (a: boolean) => void }) {
  const crear = useCrearProveedor();
  const actualizar = useActualizarProveedor();
  const [error, setErrorGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState } = useForm<Entrada, unknown, Datos>({
    resolver: zodResolver(esquema),
    values: {
      codigo: proveedor?.codigo ?? '',
      razonSocial: proveedor?.razonSocial ?? '',
      nombreComercial: proveedor?.nombreComercial ?? '',
      rfc: proveedor?.rfc ?? '',
      contacto: proveedor?.contacto ?? '',
      telefono: proveedor?.telefono ?? '',
      email: proveedor?.email ?? '',
      diasCredito: proveedor?.diasCredito ?? 0,
    },
  });
  const enviar = handleSubmit(async (d) => {
    setErrorGeneral(null);
    try {
      if (proveedor) await actualizar.mutateAsync({ id: proveedor.id, data: aSolicitud(d) });
      else await crear.mutateAsync({ data: aSolicitud(d) });
      toast.success('Proveedor guardado.');
      onCambiar(false);
    } catch (e) {
      if (esErrorApi(e)) Object.entries(e.errores).forEach(([c, m]) => setError(c as Path<Entrada>, { message: m[0] }));
      setErrorGeneral(mensajeError(e));
    }
  });
  const errores = formState.errors;
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo={proveedor ? `Editar ${proveedor.codigo}` : 'Nuevo proveedor'}
      acciones={
        <>
          <Boton variante="texto" onClick={() => onCambiar(false)}>
            Cancelar
          </Boton>
          <Boton cargando={formState.isSubmitting} onClick={enviar}>
            Guardar
          </Boton>
        </>
      }
    >
      <form onSubmit={enviar} noValidate className="grid gap-4 sm:grid-cols-2">
        {error && (
          <Aviso tipo="error" className="sm:col-span-2">
            {error}
          </Aviso>
        )}
        <Campo etiqueta="Código" error={errores.codigo?.message} {...register('codigo')} />
        <Campo etiqueta="RFC (opcional)" error={errores.rfc?.message} {...register('rfc')} />
        <Campo etiqueta="Razón social" className="sm:col-span-2" error={errores.razonSocial?.message} {...register('razonSocial')} />
        <Campo etiqueta="Nombre comercial (opcional)" className="sm:col-span-2" {...register('nombreComercial')} />
        <Campo etiqueta="Contacto (opcional)" {...register('contacto')} />
        <Campo etiqueta="Teléfono (opcional)" type="tel" inputMode="tel" {...register('telefono')} />
        <Campo etiqueta="Correo (opcional)" type="email" error={errores.email?.message} {...register('email')} />
        <Campo etiqueta="Días de crédito" type="number" inputMode="numeric" error={errores.diasCredito?.message} {...register('diasCredito')} />
      </form>
    </Dialogo>
  );
}

/** Último precio por producto e historial de compras del proveedor (RN-COM-08). */
function DialogoPrecios({ proveedor, onCerrar }: { proveedor: ProveedorDto; onCerrar: () => void }) {
  const { data, isLoading } = useListarPreciosProveedor(proveedor.id);
  const columnas = useMemo<ColumnDef<PrecioProveedorDto, unknown>[]>(
    () => [
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.producto}</span>
            <span className="text-xs text-muted">{row.original.sku}</span>
          </span>
        ),
      },
      { header: 'Unidad', accessorKey: 'unidad' },
      { header: 'Último precio', accessorKey: 'ultimoPrecio', cell: ({ row }) => formatoMoneda(row.original.ultimoPrecio), meta: { alinear: 'fin' } },
      { header: 'Última compra', accessorKey: 'ultimaCompraEn', cell: ({ row }) => formatoFechaHora(row.original.ultimaCompraEn) },
    ],
    [],
  );
  return (
    <Dialogo abierto onCambiar={(a) => !a && onCerrar()} titulo={`Precios de ${proveedor.razonSocial}`} ancho="grande" descripcion="Precios sin IVA de las recepciones registradas.">
      <TablaDatos
        columnas={columnas}
        datos={data}
        cargando={isLoading}
        idFila={(p) => `${p.productoId}-${p.unidadId}`}
        mensajeVacio="Todavía no hay compras a este proveedor."
        detalle={(p) => (
          <ul className="m-0 list-none p-0 text-sm">
            {p.historial.map((h, i) => (
              <li key={i} className="flex flex-wrap justify-between gap-2 border-b border-row-divider py-1.5 last:border-0">
                <span>
                  {formatoFecha(h.fecha)} · {h.folioRecepcion}
                </span>
                <span className="tabular-nums">
                  {formatoCantidad(h.cantidad)} {h.unidad} a {formatoMoneda(h.precioUnitario)} · costo base {formatoMoneda(h.costoUnitarioBase)}
                </span>
              </li>
            ))}
          </ul>
        )}
      />
    </Dialogo>
  );
}

export function Proveedores() {
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('compras.editar');
  const [buscar, setBuscar] = useState('');
  const [activo, setActivo] = useState('true');
  const [pagina, setPagina] = useState(1);
  const { data, isLoading, isFetching } = useListarProveedores({
    buscar: buscar || undefined,
    activo: activo === '' ? undefined : activo === 'true',
    pagina,
    tamano: 50,
  });
  const desactivar = useDesactivarProveedor();
  const activar = useActivarProveedor();
  const [dialogo, setDialogo] = useState<{ abierto: boolean; proveedor: ProveedorDto | null }>({ abierto: false, proveedor: null });
  const [precios, setPrecios] = useState<ProveedorDto | null>(null);
  const [catalogo, setCatalogo] = useState<ProveedorDto | null>(null);
  const [cambiarEstado, setCambiarEstado] = useState<ProveedorDto | null>(null);

  const columnas = useMemo<ColumnDef<ProveedorDto, unknown>[]>(
    () => [
      { header: 'Código', accessorKey: 'codigo', cell: ({ row }) => <span className="font-medium">{row.original.codigo}</span> },
      {
        header: 'Proveedor',
        accessorKey: 'razonSocial',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.razonSocial}</span>
            {row.original.nombreComercial && <span className="text-xs text-muted">{row.original.nombreComercial}</span>}
          </span>
        ),
      },
      { header: 'RFC', accessorKey: 'rfc', cell: ({ row }) => row.original.rfc ?? '—', meta: { ocultarEnMovil: true } },
      {
        header: 'Contacto',
        accessorKey: 'contacto',
        cell: ({ row }) => [row.original.contacto, row.original.telefono].filter(Boolean).join(' · ') || '—',
        meta: { ocultarEnMovil: true },
      },
      {
        header: 'Productos',
        accessorKey: 'productos',
        meta: { alinear: 'fin' },
        cell: ({ row }) => (
          <Boton variante="texto" tamano="chico" onClick={() => setCatalogo(row.original)}>
            {row.original.productos ?? 0}
          </Boton>
        ),
      },
      { header: 'Crédito', accessorKey: 'diasCredito', cell: ({ row }) => `${row.original.diasCredito} días`, meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Estado', accessorKey: 'activo', cell: ({ row }) => <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} /> },
      {
        id: 'acciones',
        header: '',
        enableSorting: false,
        meta: { alinear: 'fin' },
        cell: ({ row }) => (
          <MenuAcciones
            disparador={<BotonIcono icono="more_vert" etiqueta={`Acciones de ${row.original.codigo}`} plano />}
            opciones={[
              { etiqueta: 'Productos que vende', icono: 'inventory_2', alSeleccionar: () => setCatalogo(row.original) },
              { etiqueta: 'Historial de precios', icono: 'price_change', alSeleccionar: () => setPrecios(row.original) },
              ...(editable
                ? [
                    { etiqueta: 'Editar', icono: 'edit', alSeleccionar: () => setDialogo({ abierto: true, proveedor: row.original }) },
                    {
                      etiqueta: row.original.activo ? 'Desactivar' : 'Activar',
                      icono: row.original.activo ? 'block' : 'check_circle',
                      alSeleccionar: () => setCambiarEstado(row.original),
                    },
                  ]
                : []),
            ]}
          />
        ),
      },
    ],
    [editable],
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Proveedores"
        subtitulo="Proveedores de maíz, insumos, empaques y productos de reventa."
        acciones={
          <>
            <Boton
              variante="secundario"
              icono="download"
              disabled={!data?.items.length}
              onClick={() =>
                exportarCsv('proveedores', data?.items ?? [], [
                  { titulo: 'Código', valor: (p) => p.codigo },
                  { titulo: 'Razón social', valor: (p) => p.razonSocial },
                  { titulo: 'RFC', valor: (p) => p.rfc ?? '' },
                  { titulo: 'Teléfono', valor: (p) => p.telefono ?? '' },
                  { titulo: 'Correo', valor: (p) => p.email ?? '' },
                  { titulo: 'Días de crédito', valor: (p) => p.diasCredito },
                ])
              }
            >
              Exportar CSV
            </Boton>
            {editable && (
              <Boton icono="add" onClick={() => setDialogo({ abierto: true, proveedor: null })}>
                Nuevo proveedor
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
        idFila={(p) => p.id}
        mensajeVacio="No hay proveedores con esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
      {dialogo.abierto && <DialogoProveedor {...dialogo} onCambiar={(a) => setDialogo((d) => ({ ...d, abierto: a }))} />}
      {precios && <DialogoPrecios proveedor={precios} onCerrar={() => setPrecios(null)} />}
      {catalogo && <DialogoProductosProveedor proveedor={catalogo} editable={editable} onCerrar={() => setCatalogo(null)} />}
      {cambiarEstado && (
        <DialogoConfirmacion
          abierto
          onCambiar={(a) => !a && setCambiarEstado(null)}
          titulo={cambiarEstado.activo ? 'Desactivar proveedor' : 'Activar proveedor'}
          mensaje={
            cambiarEstado.activo
              ? `${cambiarEstado.razonSocial} ya no aparecerá para nuevas órdenes de compra.`
              : `${cambiarEstado.razonSocial} volverá a estar disponible.`
          }
          textoConfirmar={cambiarEstado.activo ? 'Desactivar' : 'Activar'}
          peligro={cambiarEstado.activo}
          alConfirmar={async () => {
            if (cambiarEstado.activo) await desactivar.mutateAsync({ id: cambiarEstado.id });
            else await activar.mutateAsync({ id: cambiarEstado.id });
            toast.success('Proveedor actualizado.');
          }}
        />
      )}
    </Pagina>
  );
}
