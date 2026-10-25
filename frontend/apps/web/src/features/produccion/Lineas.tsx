import { zodResolver } from '@hookform/resolvers/zod';
import { esErrorApi, mensajeError, useActualizarLinea, useCrearLinea, useListarLineas, type LineaDto } from '@tg/api-client';
import { Aviso, Boton, BotonIcono, Campo, Casilla, Dialogo, EtiquetaEstado, Selector, TablaDatos, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { useForm, type Path } from 'react-hook-form';
import { z } from 'zod';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';

const TIPOS = [
  { valor: 'Molino', etiqueta: 'Molino' },
  { valor: 'Tortilladora', etiqueta: 'Tortilladora' },
  { valor: 'Freidora', etiqueta: 'Freidora' },
  { valor: 'Empaque', etiqueta: 'Empaque' },
  { valor: 'Otro', etiqueta: 'Otro' },
];

const esquema = z.object({
  codigo: z.string().trim().min(1, 'Captura el código.').max(20),
  nombre: z.string().trim().min(1, 'Captura el nombre.'),
  tipo: z.enum(['Molino', 'Tortilladora', 'Freidora', 'Empaque', 'Otro']),
  activo: z.boolean(),
});
type Datos = z.infer<typeof esquema>;

function DialogoLinea({ linea, abierto, onCambiar }: { linea: LineaDto | null; abierto: boolean; onCambiar: (a: boolean) => void }) {
  const crear = useCrearLinea();
  const actualizar = useActualizarLinea();
  const [error, setErrorGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState } = useForm<Datos>({
    resolver: zodResolver(esquema),
    values: linea ? { codigo: linea.codigo, nombre: linea.nombre, tipo: linea.tipo, activo: linea.activo } : { codigo: '', nombre: '', tipo: 'Tortilladora', activo: true },
  });
  const enviar = handleSubmit(async (d) => {
    setErrorGeneral(null);
    try {
      const datos = { ...d, codigo: d.codigo.toUpperCase() };
      if (linea) await actualizar.mutateAsync({ id: linea.id, data: datos });
      else await crear.mutateAsync({ data: datos });
      toast.success('Línea guardada.');
      onCambiar(false);
    } catch (e) {
      if (esErrorApi(e)) Object.entries(e.errores).forEach(([c, m]) => setError(c as Path<Datos>, { message: m[0] }));
      setErrorGeneral(mensajeError(e));
    }
  });
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo={linea ? `Editar ${linea.codigo}` : 'Nueva línea'}
      ancho="chico"
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
      <form onSubmit={enviar} noValidate className="grid gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Campo etiqueta="Código" error={formState.errors.codigo?.message} {...register('codigo')} />
        <Campo etiqueta="Nombre" error={formState.errors.nombre?.message} {...register('nombre')} />
        <Selector etiqueta="Tipo" opciones={TIPOS} {...register('tipo')} />
        <Casilla etiqueta="Activa" {...register('activo')} />
      </form>
    </Dialogo>
  );
}

/** Líneas de producción: molinos, tortilladoras, freidoras y empaque [SUP-05]. */
export function Lineas() {
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('produccion.recetas');
  const { data, isLoading } = useListarLineas();
  const [dialogo, setDialogo] = useState<{ abierto: boolean; linea: LineaDto | null }>({ abierto: false, linea: null });
  const columnas = useMemo<ColumnDef<LineaDto, unknown>[]>(
    () => [
      { header: 'Código', accessorKey: 'codigo', cell: ({ row }) => <span className="font-medium">{row.original.codigo}</span> },
      { header: 'Nombre', accessorKey: 'nombre' },
      { header: 'Tipo', accessorKey: 'tipo' },
      { header: 'Estado', accessorKey: 'activo', cell: ({ row }) => <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} /> },
      ...(editable
        ? ([
            {
              id: 'acciones',
              header: '',
              enableSorting: false,
              meta: { alinear: 'fin' },
              cell: ({ row }) => (
                <BotonIcono icono="edit" etiqueta={`Editar ${row.original.codigo}`} onClick={() => setDialogo({ abierto: true, linea: row.original })} />
              ),
            },
          ] as ColumnDef<LineaDto, unknown>[])
        : []),
    ],
    [editable],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Líneas de producción"
        subtitulo="Equipos donde se planean las órdenes del día."
        acciones={
          editable && (
            <Boton icono="add" onClick={() => setDialogo({ abierto: true, linea: null })}>
              Nueva línea
            </Boton>
          )
        }
      />
      <TablaDatos columnas={columnas} datos={data} cargando={isLoading} idFila={(l) => l.id} />
      {dialogo.abierto && <DialogoLinea {...dialogo} onCambiar={(a) => setDialogo((d) => ({ ...d, abierto: a }))} />}
    </Pagina>
  );
}
