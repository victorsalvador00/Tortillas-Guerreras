import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate, useParams } from '@tanstack/react-router';
import {
  esErrorApi,
  mensajeError,
  useActivarProducto,
  useActualizarProducto,
  useCrearProducto,
  useDesactivarProducto,
  useListarUnidades,
  useObtenerProducto,
  type GuardarProductoRequest,
  type ProductoDto,
  type TipoProducto,
} from '@tg/api-client';
import { tasa } from '@tg/shared';
import { Aviso, BarraAccion, Boton, BotonIcono, Campo, Casilla, DialogoConfirmacion, Selector, Tarjeta, toast } from '@tg/ui';
import { useEffect, useState } from 'react';
import { Controller, useFieldArray, useForm, type Path } from 'react-hook-form';
import { z } from 'zod';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { TIPOS_PRODUCTO } from './Productos';

/** RN-CAT-04: banderas propuestas por tipo (editables). */
const BANDERAS: Record<TipoProducto, { esComprable: boolean; esProducible: boolean; esVendible: boolean; manejaLote: boolean }> = {
  MateriaPrima: { esComprable: true, esProducible: false, esVendible: false, manejaLote: false },
  Empaque: { esComprable: true, esProducible: false, esVendible: false, manejaLote: false },
  Semiterminado: { esComprable: false, esProducible: true, esVendible: false, manejaLote: false },
  Terminado: { esComprable: false, esProducible: true, esVendible: true, manejaLote: true },
  Reventa: { esComprable: true, esProducible: false, esVendible: true, manejaLote: false },
};

const enteroOpcional = z.preprocess((v) => (v === '' || v == null ? null : Number(v)), z.number().int().positive('Debe ser mayor que cero.').nullable());
const decimalOpcional = z.preprocess((v) => (v === '' || v == null ? null : Number(v)), z.number().min(0, 'No puede ser negativo.').nullable());

const esquema = z.object({
  sku: z.string().trim().min(1, 'Captura el SKU.').max(40),
  nombre: z.string().trim().min(1, 'Captura el nombre.'),
  descripcion: z.string().optional(),
  tipo: z.enum(['MateriaPrima', 'Empaque', 'Semiterminado', 'Terminado', 'Reventa']),
  unidadBaseId: z.string().min(1, 'Elige la unidad base.'),
  esComprable: z.boolean(),
  esProducible: z.boolean(),
  esVendible: z.boolean(),
  manejaLote: z.boolean(),
  vidaUtilDias: enteroOpcional,
  claveProdServSat: z.string().optional(),
  objetoImpuestoSat: z.string().optional(),
  ivaPct: z.coerce.number().min(0).max(100),
  iepsPct: z.coerce.number().min(0).max(100),
  codigoBarras: z.string().optional(),
  stockMinimo: decimalOpcional,
  conversiones: z
    .array(
      z.object({
        unidadId: z.string().min(1, 'Elige la unidad.'),
        factorABase: z.coerce.number().positive('El factor debe ser mayor que cero.'),
        usoCompra: z.boolean(),
        usoVenta: z.boolean(),
        usoProduccion: z.boolean(),
        predeterminadaVenta: z.boolean(),
      }),
    )
    .refine((c) => c.filter((x) => x.predeterminadaVenta).length <= 1, 'Solo una unidad puede ser la predeterminada de venta.'),
});

type Entrada = z.input<typeof esquema>;
type Datos = z.output<typeof esquema>;

function aFormulario(p?: ProductoDto): Entrada {
  return {
    sku: p?.sku ?? '',
    nombre: p?.nombre ?? '',
    descripcion: p?.descripcion ?? '',
    tipo: p?.tipo ?? 'MateriaPrima',
    unidadBaseId: p?.unidadBaseId ?? '',
    esComprable: p?.esComprable ?? true,
    esProducible: p?.esProducible ?? false,
    esVendible: p?.esVendible ?? false,
    manejaLote: p?.manejaLote ?? false,
    vidaUtilDias: p?.vidaUtilDias ?? '',
    claveProdServSat: p?.claveProdServSat ?? '',
    objetoImpuestoSat: p?.objetoImpuestoSat ?? '02',
    ivaPct: p ? Math.round(p.tasaIva * 10000) / 100 : 0,
    iepsPct: p?.tasaIeps ? Math.round(p.tasaIeps * 10000) / 100 : 0,
    codigoBarras: p?.codigoBarras ?? '',
    stockMinimo: p?.stockMinimo ?? '',
    conversiones:
      p?.conversiones.map((c) => ({
        unidadId: c.unidadId,
        factorABase: c.factorABase,
        usoCompra: c.usoCompra,
        usoVenta: c.usoVenta,
        usoProduccion: c.usoProduccion,
        predeterminadaVenta: c.predeterminadaVenta,
      })) ?? [],
  };
}

function aSolicitud(d: Datos): GuardarProductoRequest {
  return {
    sku: d.sku,
    nombre: d.nombre,
    descripcion: d.descripcion || null,
    tipo: d.tipo,
    unidadBaseId: d.unidadBaseId,
    esComprable: d.esComprable,
    esProducible: d.esProducible,
    esVendible: d.esVendible,
    manejaLote: d.manejaLote,
    vidaUtilDias: d.vidaUtilDias,
    claveProdServSat: d.claveProdServSat || null,
    objetoImpuestoSat: d.objetoImpuestoSat || '02',
    tasaIva: tasa.parse(d.ivaPct / 100),
    tasaIeps: d.iepsPct ? d.iepsPct / 100 : null,
    codigoBarras: d.codigoBarras || null,
    stockMinimo: d.stockMinimo,
    conversiones: d.conversiones,
  };
}

export function ProductoFormulario() {
  const { id } = useParams({ strict: false }) as { id?: string };
  const nuevo = !id;
  const consulta = useObtenerProducto(id ?? '', { query: { enabled: !nuevo } });
  const unidades = useListarUnidades();
  return (
    <Pagina ancho="medio">
      {/* Las listas nativas necesitan sus opciones antes de recibir el valor inicial. */}
      <EstadoConsulta cargando={(!nuevo && consulta.isLoading) || unidades.isLoading} error={consulta.error ?? unidades.error}>
        <FormularioProducto key={consulta.data?.id ?? 'nuevo'} producto={consulta.data} />
      </EstadoConsulta>
    </Pagina>
  );
}

function FormularioProducto({ producto }: { producto?: ProductoDto }) {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('catalogos.editar');
  const { data: unidades } = useListarUnidades();
  const crear = useCrearProducto();
  const actualizar = useActualizarProducto();
  const desactivar = useDesactivarProducto();
  const activar = useActivarProducto();
  const [errorGeneral, setErrorGeneral] = useState<string | null>(null);
  const [confirmar, setConfirmar] = useState(false);

  const {
    register,
    control,
    handleSubmit,
    watch,
    setValue,
    setError,
    getFieldState,
    formState: { errors, isSubmitting },
  } = useForm<Entrada, unknown, Datos>({ resolver: zodResolver(esquema), defaultValues: aFormulario(producto) });
  const conversiones = useFieldArray({ control, name: 'conversiones' });
  const tipo = watch('tipo');
  const unidadBaseId = watch('unidadBaseId');

  // RN-CAT-04: al cambiar el tipo en un producto nuevo se proponen las banderas.
  useEffect(() => {
    if (producto) return;
    const b = BANDERAS[tipo];
    (Object.keys(b) as (keyof typeof b)[]).forEach((k) => {
      if (!getFieldState(k).isDirty) setValue(k, b[k]);
    });
  }, [tipo, producto, setValue, getFieldState]);

  const opcionesUnidad = (unidades ?? []).filter((u) => u.activo).map((u) => ({ valor: u.id, etiqueta: `${u.clave} · ${u.nombre}` }));
  const claveBase = unidades?.find((u) => u.id === unidadBaseId)?.clave ?? 'unidad base';

  const enviar = handleSubmit(async (d) => {
    setErrorGeneral(null);
    try {
      const datos = aSolicitud(d);
      const guardado = producto
        ? await actualizar.mutateAsync({ id: producto.id, data: datos })
        : await crear.mutateAsync({ data: datos });
      toast.success(producto ? 'Producto actualizado.' : 'Producto creado.');
      if (!producto) await navegar({ to: '/catalogos/productos/$id', params: { id: guardado.id } });
    } catch (e) {
      if (esErrorApi(e) && Object.keys(e.errores).length > 0) {
        Object.entries(e.errores).forEach(([campo, mensajes]) => setError(campo as Path<Entrada>, { message: mensajes[0] }));
      }
      setErrorGeneral(mensajeError(e));
    }
  });

  return (
    <>
      <EncabezadoPagina
        titulo={producto ? producto.nombre : 'Nuevo producto'}
        subtitulo={producto ? `${producto.sku} · ${producto.activo ? 'Activo' : 'Inactivo'}` : 'Las banderas de uso se proponen según el tipo; puedes cambiarlas.'}
        migas={[{ etiqueta: 'Catálogos' }, { etiqueta: 'Productos', a: '/catalogos/productos' }, { etiqueta: producto ? producto.sku : 'Nuevo' }]}
        acciones={
          producto &&
          editable && (
            <Boton variante={producto.activo ? 'secundario' : 'primario'} icono={producto.activo ? 'block' : 'check_circle'} onClick={() => setConfirmar(true)}>
              {producto.activo ? 'Desactivar' : 'Activar'}
            </Boton>
          )
        }
      />
      <form onSubmit={enviar} noValidate className="flex flex-col gap-4">
        {errorGeneral && <Aviso tipo="error">{errorGeneral}</Aviso>}
        <fieldset disabled={!editable} className="m-0 flex min-w-0 flex-col gap-4 border-0 p-0">
          <Tarjeta titulo="Datos generales">
            <div className="grid gap-4 sm:grid-cols-2">
              <Campo etiqueta="SKU" error={errors.sku?.message} ayuda="No se puede cambiar si el producto ya tiene movimientos." {...register('sku')} />
              <Campo etiqueta="Nombre" error={errors.nombre?.message} {...register('nombre')} />
              <Selector etiqueta="Tipo" opciones={TIPOS_PRODUCTO} error={errors.tipo?.message} {...register('tipo')} />
              <Selector
                etiqueta="Unidad base (inventario)"
                vacio="Elige la unidad"
                opciones={opcionesUnidad}
                error={errors.unidadBaseId?.message}
                ayuda="Unidad en la que se lleva la existencia."
                {...register('unidadBaseId')}
              />
              <Campo etiqueta="Descripción (opcional)" className="sm:col-span-2" {...register('descripcion')} />
              <Campo etiqueta="Código de barras (opcional)" {...register('codigoBarras')} />
            </div>
            <div className="mt-4 flex flex-wrap gap-x-8 gap-y-1">
              <Casilla etiqueta="Se compra" {...register('esComprable')} />
              <Casilla etiqueta="Se produce" {...register('esProducible')} />
              <Casilla etiqueta="Se vende" {...register('esVendible')} />
              <Casilla etiqueta="Maneja lote" descripcion="Entradas con lote; salidas por FEFO." {...register('manejaLote')} />
            </div>
          </Tarjeta>

          <Tarjeta
            titulo="Unidades y conversiones"
            subtitulo={`Factor a ${claveBase}: 1 TON = 1000 KG, 1 BUL = 20 KG. La unidad base tiene factor 1.`}
            acciones={
              editable && (
                <Boton
                  variante="secundario"
                  tamano="chico"
                  icono="add"
                  onClick={() =>
                    conversiones.append({ unidadId: '', factorABase: 1, usoCompra: true, usoVenta: false, usoProduccion: false, predeterminadaVenta: false })
                  }
                >
                  Agregar unidad
                </Boton>
              )
            }
          >
            {conversiones.fields.length === 0 && <p className="m-0 text-sm text-muted">Solo se usa la unidad base.</p>}
            <div className="flex flex-col gap-3">
              {conversiones.fields.map((f, i) => (
                <div key={f.id} className="grid items-start gap-3 rounded-xl border border-row-divider p-3 sm:grid-cols-[1fr_180px_auto]">
                  <Selector
                    etiqueta="Unidad"
                    vacio="Elige"
                    opciones={opcionesUnidad.filter((o) => o.valor !== unidadBaseId)}
                    error={errors.conversiones?.[i]?.unidadId?.message}
                    {...register(`conversiones.${i}.unidadId`)}
                  />
                  <Campo
                    etiqueta={`Factor a ${claveBase}`}
                    type="number"
                    step="any"
                    inputMode="decimal"
                    error={errors.conversiones?.[i]?.factorABase?.message}
                    {...register(`conversiones.${i}.factorABase`)}
                  />
                  {editable ? <BotonIcono icono="delete" etiqueta="Quitar unidad" className="mt-2" onClick={() => conversiones.remove(i)} /> : <span />}
                  <div className="flex flex-wrap gap-x-4 sm:col-span-3">
                    <Casilla etiqueta="Compra" {...register(`conversiones.${i}.usoCompra`)} />
                    <Casilla etiqueta="Venta" {...register(`conversiones.${i}.usoVenta`)} />
                    <Casilla etiqueta="Producción" {...register(`conversiones.${i}.usoProduccion`)} />
                    <Controller
                      control={control}
                      name={`conversiones.${i}.predeterminadaVenta`}
                      render={({ field }) => (
                        <Casilla
                          etiqueta="Predeterminada de venta"
                          checked={!!field.value}
                          onChange={(e) => {
                            if (e.target.checked) {
                              conversiones.fields.forEach((_, j) => j !== i && setValue(`conversiones.${j}.predeterminadaVenta`, false));
                            }
                            field.onChange(e.target.checked);
                          }}
                        />
                      )}
                    />
                  </div>
                </div>
              ))}
            </div>
            {errors.conversiones?.root?.message && <p className="mt-2 text-sm text-error">{errors.conversiones.root.message}</p>}
            {errors.conversiones?.message && <p className="mt-2 text-sm text-error">{errors.conversiones.message}</p>}
          </Tarjeta>

          <Tarjeta titulo="Impuestos y SAT" subtitulo="Tortilla y masa: IVA 0 %; botanas: IVA 16 % + IEPS 8 % [SUP-03, por confirmar con el contador].">
            <div className="grid gap-4 sm:grid-cols-2">
              <Campo etiqueta="IVA" type="number" step="any" sufijo="%" error={errors.ivaPct?.message} {...register('ivaPct')} />
              <Campo etiqueta="IEPS" type="number" step="any" sufijo="%" error={errors.iepsPct?.message} {...register('iepsPct')} />
              <Campo etiqueta="Clave de producto o servicio SAT" ayuda="Sin clave no se puede facturar (FAC-006)." {...register('claveProdServSat')} />
              <Campo etiqueta="Objeto de impuesto SAT" {...register('objetoImpuestoSat')} />
            </div>
          </Tarjeta>

          <Tarjeta titulo="Inventario">
            <div className="grid gap-4 sm:grid-cols-2">
              <Campo
                etiqueta="Vida útil (días)"
                type="number"
                inputMode="numeric"
                ayuda="Con lote, la caducidad se calcula con este dato."
                error={errors.vidaUtilDias?.message}
                {...register('vidaUtilDias')}
              />
              <Campo
                etiqueta={`Existencia mínima (${claveBase})`}
                type="number"
                step="any"
                ayuda="Alerta en el tablero."
                error={errors.stockMinimo?.message}
                {...register('stockMinimo')}
              />
            </div>
          </Tarjeta>
        </fieldset>
        {editable && (
          <BarraAccion resumen={producto ? 'Los cambios bajan a la app de ruta en la siguiente sincronización.' : undefined}>
            <Boton variante="texto" onClick={() => navegar({ to: '/catalogos/productos' })}>
              Cancelar
            </Boton>
            <Boton type="submit" cargando={isSubmitting}>
              {producto ? 'Guardar cambios' : 'Crear producto'}
            </Boton>
          </BarraAccion>
        )}
      </form>

      {producto && (
        <DialogoConfirmacion
          abierto={confirmar}
          onCambiar={setConfirmar}
          titulo={producto.activo ? 'Desactivar producto' : 'Activar producto'}
          mensaje={
            producto.activo
              ? `${producto.nombre} dejará de aparecer en compras, recetas y ventas. Solo se puede desactivar si no tiene existencia.`
              : `${producto.nombre} volverá a estar disponible.`
          }
          textoConfirmar={producto.activo ? 'Desactivar' : 'Activar'}
          peligro={producto.activo}
          alConfirmar={async () => {
            if (producto.activo) await desactivar.mutateAsync({ id: producto.id });
            else await activar.mutateAsync({ id: producto.id });
            toast.success(producto.activo ? 'Producto desactivado.' : 'Producto activado.');
          }}
        />
      )}
    </>
  );
}
