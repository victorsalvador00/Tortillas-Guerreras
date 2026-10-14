import { zodResolver } from '@hookform/resolvers/zod';
import {
  esErrorApi,
  mensajeError,
  useActivarMotivoMerma,
  useActivarUnidad,
  useActualizarMotivoMerma,
  useActualizarParametro,
  useActualizarUnidad,
  useCrearMotivoMerma,
  useCrearUnidad,
  useDesactivarMotivoMerma,
  useDesactivarUnidad,
  useListarMotivosMerma,
  useListarParametros,
  useListarUnidades,
  type MotivoMermaDto,
  type ParametroDto,
  type UnidadDto,
} from '@tg/api-client';
import { Aviso, Boton, BotonIcono, Campo, Casilla, Dialogo, EtiquetaEstado, MenuAcciones, Selector, TablaDatos, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { useForm, type Path } from 'react-hook-form';
import { z } from 'zod';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';

function aplicarErrores<T extends Record<string, unknown>>(e: unknown, setError: (c: Path<T>, v: { message: string }) => void): string {
  if (esErrorApi(e)) {
    Object.entries(e.errores).forEach(([campo, m]) => setError(campo as Path<T>, { message: m[0] }));
  }
  return mensajeError(e);
}

// ───────────────────────────── Unidades ─────────────────────────────

const esquemaUnidad = z.object({
  clave: z.string().trim().min(1, 'Captura la clave.').max(10),
  nombre: z.string().trim().min(1, 'Captura el nombre.'),
  tipo: z.enum(['Masa', 'Volumen', 'Pieza']),
  claveSat: z.string().trim().min(1, 'Captura la clave SAT (KGM, H87…).'),
  decimales: z.coerce.number().int().min(0).max(4),
});
type DatosUnidad = z.output<typeof esquemaUnidad>;

function DialogoUnidad({ unidad, abierto, onCambiar }: { unidad: UnidadDto | null; abierto: boolean; onCambiar: (a: boolean) => void }) {
  const crear = useCrearUnidad();
  const actualizar = useActualizarUnidad();
  const [error, setErrorGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState } = useForm<z.input<typeof esquemaUnidad>, unknown, DatosUnidad>({
    resolver: zodResolver(esquemaUnidad),
    values: unidad
      ? { clave: unidad.clave, nombre: unidad.nombre, tipo: unidad.tipo, claveSat: unidad.claveSat, decimales: unidad.decimales }
      : { clave: '', nombre: '', tipo: 'Masa', claveSat: '', decimales: 3 },
  });
  const enviar = handleSubmit(async (d) => {
    try {
      if (unidad) await actualizar.mutateAsync({ id: unidad.id, data: d });
      else await crear.mutateAsync({ data: d });
      toast.success('Unidad guardada.');
      onCambiar(false);
    } catch (e) {
      setErrorGeneral(aplicarErrores<DatosUnidad>(e, setError));
    }
  });
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo={unidad ? `Editar ${unidad.clave}` : 'Nueva unidad'}
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
      <form onSubmit={enviar} className="grid gap-4 sm:grid-cols-2">
        {error && <Aviso tipo="error" className="sm:col-span-2">{error}</Aviso>}
        <Campo etiqueta="Clave" error={formState.errors.clave?.message} {...register('clave')} />
        <Campo etiqueta="Nombre" error={formState.errors.nombre?.message} {...register('nombre')} />
        <Selector
          etiqueta="Tipo"
          opciones={[
            { valor: 'Masa', etiqueta: 'Masa (peso)' },
            { valor: 'Volumen', etiqueta: 'Volumen' },
            { valor: 'Pieza', etiqueta: 'Pieza' },
          ]}
          {...register('tipo')}
        />
        <Campo etiqueta="Clave de unidad SAT" error={formState.errors.claveSat?.message} {...register('claveSat')} />
        <Campo
          etiqueta="Decimales en captura"
          type="number"
          ayuda="KG = 3, PZA = 0 (RN-CAT-06)."
          error={formState.errors.decimales?.message}
          {...register('decimales')}
        />
      </form>
    </Dialogo>
  );
}

export function Unidades() {
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('catalogos.editar');
  const { data, isLoading } = useListarUnidades();
  const desactivar = useDesactivarUnidad();
  const activar = useActivarUnidad();
  const [dialogo, setDialogo] = useState<{ abierto: boolean; unidad: UnidadDto | null }>({ abierto: false, unidad: null });
  const columnas = useMemo<ColumnDef<UnidadDto, unknown>[]>(
    () => [
      { header: 'Clave', accessorKey: 'clave', cell: ({ row }) => <span className="font-medium">{row.original.clave}</span> },
      { header: 'Nombre', accessorKey: 'nombre' },
      { header: 'Tipo', accessorKey: 'tipo' },
      { header: 'Clave SAT', accessorKey: 'claveSat' },
      { header: 'Decimales', accessorKey: 'decimales', meta: { alinear: 'fin' } },
      { header: 'Estado', accessorKey: 'activo', cell: ({ row }) => <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} /> },
      ...(editable
        ? ([
            {
              id: 'acciones',
              header: '',
              enableSorting: false,
              meta: { alinear: 'fin' },
              cell: ({ row }) => (
                <MenuAcciones
                  disparador={<BotonIcono icono="more_vert" etiqueta={`Acciones de ${row.original.clave}`} plano />}
                  opciones={[
                    { etiqueta: 'Editar', icono: 'edit', alSeleccionar: () => setDialogo({ abierto: true, unidad: row.original }) },
                    row.original.activo
                      ? { etiqueta: 'Desactivar', icono: 'block', alSeleccionar: () => void desactivar.mutateAsync({ id: row.original.id }).catch((e) => toast.error(mensajeError(e))) }
                      : { etiqueta: 'Activar', icono: 'check_circle', alSeleccionar: () => void activar.mutateAsync({ id: row.original.id }).catch((e) => toast.error(mensajeError(e))) },
                  ]}
                />
              ),
            },
          ] as ColumnDef<UnidadDto, unknown>[])
        : []),
    ],
    [editable, desactivar, activar],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Unidades de medida"
        subtitulo="Unidades de captura con su clave del SAT y decimales permitidos."
        acciones={editable && <Boton icono="add" onClick={() => setDialogo({ abierto: true, unidad: null })}>Nueva unidad</Boton>}
      />
      <TablaDatos columnas={columnas} datos={data} cargando={isLoading} idFila={(u) => u.id} />
      <DialogoUnidad {...dialogo} onCambiar={(a) => setDialogo((d) => ({ ...d, abierto: a }))} />
    </Pagina>
  );
}

// ───────────────────────────── Motivos de merma ─────────────────────────────

const esquemaMotivo = z
  .object({
    clave: z.string().trim().min(1, 'Captura la clave.').max(30),
    nombre: z.string().trim().min(1, 'Captura el nombre.'),
    aplicaProduccion: z.boolean(),
    aplicaAlmacen: z.boolean(),
    aplicaRuta: z.boolean(),
    aplicaRetorno: z.boolean(),
  })
  .refine((d) => d.aplicaProduccion || d.aplicaAlmacen || d.aplicaRuta || d.aplicaRetorno, {
    path: ['aplicaProduccion'],
    message: 'Marca al menos dónde aplica.',
  });
type DatosMotivo = z.infer<typeof esquemaMotivo>;

function DialogoMotivo({ motivo, abierto, onCambiar }: { motivo: MotivoMermaDto | null; abierto: boolean; onCambiar: (a: boolean) => void }) {
  const crear = useCrearMotivoMerma();
  const actualizar = useActualizarMotivoMerma();
  const [error, setErrorGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState } = useForm<DatosMotivo>({
    resolver: zodResolver(esquemaMotivo),
    values: motivo
      ? { ...motivo }
      : { clave: '', nombre: '', aplicaProduccion: false, aplicaAlmacen: true, aplicaRuta: false, aplicaRetorno: false },
  });
  const enviar = handleSubmit(async (d) => {
    try {
      if (motivo) await actualizar.mutateAsync({ id: motivo.id, data: d });
      else await crear.mutateAsync({ data: d });
      toast.success('Motivo guardado.');
      onCambiar(false);
    } catch (e) {
      setErrorGeneral(aplicarErrores<DatosMotivo>(e, setError));
    }
  });
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo={motivo ? `Editar motivo` : 'Nuevo motivo de merma'}
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
      <form onSubmit={enviar} className="grid gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <div className="grid gap-4 sm:grid-cols-2">
          <Campo etiqueta="Clave" disabled={!!motivo} error={formState.errors.clave?.message} {...register('clave')} />
          <Campo etiqueta="Nombre" error={formState.errors.nombre?.message} {...register('nombre')} />
        </div>
        <fieldset className="m-0 border-0 p-0">
          <legend className="mb-1 text-sm font-medium">Aplica en</legend>
          <div className="grid grid-cols-2 gap-1">
            <Casilla etiqueta="Producción" {...register('aplicaProduccion')} />
            <Casilla etiqueta="Almacén" {...register('aplicaAlmacen')} />
            <Casilla etiqueta="Ruta" {...register('aplicaRuta')} />
            <Casilla etiqueta="Retornos" {...register('aplicaRetorno')} />
          </div>
          {formState.errors.aplicaProduccion && <p className="m-0 mt-1 text-xs text-error">{formState.errors.aplicaProduccion.message}</p>}
        </fieldset>
      </form>
    </Dialogo>
  );
}

export function MotivosMerma() {
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('catalogos.editar');
  const { data, isLoading } = useListarMotivosMerma();
  const desactivar = useDesactivarMotivoMerma();
  const activar = useActivarMotivoMerma();
  const [dialogo, setDialogo] = useState<{ abierto: boolean; motivo: MotivoMermaDto | null }>({ abierto: false, motivo: null });
  const si = (v: boolean) => (v ? <span className="icono text-20 text-[var(--tg-status-green-fg)]">check</span> : <span className="text-muted">—</span>);
  const columnas = useMemo<ColumnDef<MotivoMermaDto, unknown>[]>(
    () => [
      { header: 'Clave', accessorKey: 'clave', cell: ({ row }) => <span className="font-medium">{row.original.clave}</span> },
      { header: 'Nombre', accessorKey: 'nombre' },
      { header: 'Producción', accessorKey: 'aplicaProduccion', cell: ({ row }) => si(row.original.aplicaProduccion), meta: { alinear: 'centro' } },
      { header: 'Almacén', accessorKey: 'aplicaAlmacen', cell: ({ row }) => si(row.original.aplicaAlmacen), meta: { alinear: 'centro' } },
      { header: 'Ruta', accessorKey: 'aplicaRuta', cell: ({ row }) => si(row.original.aplicaRuta), meta: { alinear: 'centro' } },
      { header: 'Retornos', accessorKey: 'aplicaRetorno', cell: ({ row }) => si(row.original.aplicaRetorno), meta: { alinear: 'centro' } },
      { header: 'Estado', accessorKey: 'activo', cell: ({ row }) => <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} /> },
      ...(editable
        ? ([
            {
              id: 'acciones',
              header: '',
              enableSorting: false,
              meta: { alinear: 'fin' },
              cell: ({ row }) => (
                <MenuAcciones
                  disparador={<BotonIcono icono="more_vert" etiqueta={`Acciones de ${row.original.nombre}`} plano />}
                  opciones={[
                    { etiqueta: 'Editar', icono: 'edit', alSeleccionar: () => setDialogo({ abierto: true, motivo: row.original }) },
                    row.original.activo
                      ? { etiqueta: 'Desactivar', icono: 'block', alSeleccionar: () => void desactivar.mutateAsync({ id: row.original.id }) }
                      : { etiqueta: 'Activar', icono: 'check_circle', alSeleccionar: () => void activar.mutateAsync({ id: row.original.id }) },
                  ]}
                />
              ),
            },
          ] as ColumnDef<MotivoMermaDto, unknown>[])
        : []),
    ],
    [editable, desactivar, activar],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Motivos de merma"
        subtitulo="Motivos para mermas de producción, ajustes de almacén, ruta y retornos."
        acciones={editable && <Boton icono="add" onClick={() => setDialogo({ abierto: true, motivo: null })}>Nuevo motivo</Boton>}
      />
      <TablaDatos columnas={columnas} datos={data} cargando={isLoading} idFila={(m) => m.id} />
      <DialogoMotivo {...dialogo} onCambiar={(a) => setDialogo((d) => ({ ...d, abierto: a }))} />
    </Pagina>
  );
}

// ───────────────────────────── Parámetros ─────────────────────────────

function DialogoParametro({ parametro, onCerrar }: { parametro: ParametroDto | null; onCerrar: () => void }) {
  const actualizar = useActualizarParametro();
  const [valor, setValor] = useState(parametro?.valor ?? '');
  const [error, setError] = useState<string | null>(null);
  if (!parametro) return null;
  const guardar = async () => {
    setError(null);
    try {
      await actualizar.mutateAsync({ clave: parametro.clave, data: { valor } });
      toast.success('Parámetro actualizado.');
      onCerrar();
    } catch (e) {
      setError(esErrorApi(e) ? (e.errorDe('valor') ?? e.message) : mensajeError(e));
    }
  };
  return (
    <Dialogo
      abierto
      onCambiar={(a) => !a && onCerrar()}
      titulo={parametro.clave}
      descripcion={parametro.descripcion}
      ancho="chico"
      acciones={
        <>
          <Boton variante="texto" onClick={onCerrar}>
            Cancelar
          </Boton>
          <Boton cargando={actualizar.isPending} onClick={guardar}>
            Guardar
          </Boton>
        </>
      }
    >
      {parametro.tipo === 'bool' ? (
        <Selector
          etiqueta="Valor"
          opciones={[
            { valor: 'true', etiqueta: 'Sí' },
            { valor: 'false', etiqueta: 'No' },
          ]}
          value={valor}
          onChange={(e) => setValor(e.target.value)}
          error={error ?? undefined}
        />
      ) : (
        <Campo
          etiqueta="Valor"
          value={valor}
          inputMode={parametro.tipo === 'text' ? 'text' : 'decimal'}
          onChange={(e) => setValor(e.target.value)}
          error={error ?? undefined}
          autoFocus
        />
      )}
    </Dialogo>
  );
}

export function Parametros() {
  const { data, isLoading } = useListarParametros();
  const [editando, setEditando] = useState<ParametroDto | null>(null);
  const columnas = useMemo<ColumnDef<ParametroDto, unknown>[]>(
    () => [
      { header: 'Clave', accessorKey: 'clave', cell: ({ row }) => <code className="text-13">{row.original.clave}</code> },
      { header: 'Descripción', accessorKey: 'descripcion', meta: { ocultarEnMovil: true } },
      {
        header: 'Valor',
        accessorKey: 'valor',
        cell: ({ row }) => (
          <span className="font-medium">
            {row.original.tipo === 'bool' ? (row.original.valor === 'true' ? 'Sí' : 'No') : row.original.valor}
          </span>
        ),
      },
      {
        id: 'acciones',
        header: '',
        enableSorting: false,
        meta: { alinear: 'fin' },
        cell: ({ row }) => <BotonIcono icono="edit" etiqueta={`Editar ${row.original.clave}`} onClick={() => setEditando(row.original)} />,
      },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Parámetros"
        subtitulo="Valores de negocio configurables. Los marcados [SUP-xx] están pendientes de confirmar con el cliente."
      />
      <TablaDatos columnas={columnas} datos={data} cargando={isLoading} idFila={(p) => p.clave} />
      <DialogoParametro key={editando?.clave} parametro={editando} onCerrar={() => setEditando(null)} />
    </Pagina>
  );
}
