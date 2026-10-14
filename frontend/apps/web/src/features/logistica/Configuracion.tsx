import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import {
  useActualizarRuta,
  useActualizarVehiculo,
  useCrearRuta,
  useCrearVehiculo,
  useListarRutas,
  useListarUsuarios,
  useListarVehiculos,
  type RutaDto,
  type VehiculoDto,
} from '@tg/api-client';
import { formatoCantidad } from '@tg/shared';
import { Aviso, Boton, BotonIcono, Campo, Casilla, Dialogo, EtiquetaEstado, Selector, TablaDatos, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { aplicarErroresApi } from './comunes-logistica';

// ───────────────────────────── Vehículos ─────────────────────────────

const esquemaVehiculo = z.object({
  placas: z.string().trim().min(1, 'Captura las placas.').max(15),
  descripcion: z.string().trim().min(1, 'Describe el vehículo.'),
  capacidadKg: z.preprocess((v) => (v === '' || v == null ? null : Number(v)), z.number().positive('Debe ser mayor que cero.').nullable()),
  activo: z.boolean(),
});
type DatosVehiculo = z.output<typeof esquemaVehiculo>;

function DialogoVehiculo({ vehiculo, abierto, onCambiar }: { vehiculo: VehiculoDto | null; abierto: boolean; onCambiar: (a: boolean) => void }) {
  const crear = useCrearVehiculo();
  const actualizar = useActualizarVehiculo();
  const [error, setErrorGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState } = useForm<z.input<typeof esquemaVehiculo>, unknown, DatosVehiculo>({
    resolver: zodResolver(esquemaVehiculo),
    values: vehiculo
      ? { placas: vehiculo.placas, descripcion: vehiculo.descripcion, capacidadKg: vehiculo.capacidadKg ?? '', activo: vehiculo.activo }
      : { placas: '', descripcion: '', capacidadKg: '', activo: true },
  });
  const enviar = handleSubmit(async (d) => {
    setErrorGeneral(null);
    try {
      if (vehiculo) await actualizar.mutateAsync({ id: vehiculo.id, data: d });
      else await crear.mutateAsync({ data: d });
      toast.success('Vehículo guardado.');
      onCambiar(false);
    } catch (e) {
      setErrorGeneral(aplicarErroresApi<DatosVehiculo>(e, setError));
    }
  });
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo={vehiculo ? `Editar ${vehiculo.placas}` : 'Nuevo vehículo'}
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
        {error && (
          <Aviso tipo="error" className="sm:col-span-2">
            {error}
          </Aviso>
        )}
        <Campo etiqueta="Placas" error={formState.errors.placas?.message} {...register('placas')} />
        <Campo etiqueta="Capacidad (kg)" type="number" step="any" error={formState.errors.capacidadKg?.message} {...register('capacidadKg')} />
        <Campo etiqueta="Descripción" className="sm:col-span-2" error={formState.errors.descripcion?.message} {...register('descripcion')} />
        <Casilla etiqueta="Activo" {...register('activo')} />
      </form>
    </Dialogo>
  );
}

export function Vehiculos() {
  const { data, isLoading } = useListarVehiculos();
  const [dialogo, setDialogo] = useState<{ abierto: boolean; vehiculo: VehiculoDto | null }>({ abierto: false, vehiculo: null });
  const columnas = useMemo<ColumnDef<VehiculoDto, unknown>[]>(
    () => [
      { header: 'Placas', accessorKey: 'placas', cell: ({ row }) => <span className="font-medium">{row.original.placas}</span> },
      { header: 'Descripción', accessorKey: 'descripcion' },
      {
        header: 'Capacidad',
        accessorKey: 'capacidadKg',
        cell: ({ row }) => (row.original.capacidadKg != null ? `${formatoCantidad(row.original.capacidadKg)} kg` : '—'),
        meta: { alinear: 'fin' },
      },
      { header: 'Estado', accessorKey: 'activo', cell: ({ row }) => <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} /> },
      {
        id: 'acciones',
        header: '',
        enableSorting: false,
        meta: { alinear: 'fin' },
        cell: ({ row }) => (
          <BotonIcono icono="edit" etiqueta={`Editar ${row.original.placas}`} onClick={() => setDialogo({ abierto: true, vehiculo: row.original })} />
        ),
      },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Vehículos"
        subtitulo="Camionetas de reparto asignables a las rutas."
        acciones={
          <Boton icono="add" onClick={() => setDialogo({ abierto: true, vehiculo: null })}>
            Nuevo vehículo
          </Boton>
        }
      />
      <TablaDatos columnas={columnas} datos={data} cargando={isLoading} idFila={(v) => v.id} mensajeVacio="No hay vehículos registrados." />
      <DialogoVehiculo {...dialogo} onCambiar={(a) => setDialogo((d) => ({ ...d, abierto: a }))} />
    </Pagina>
  );
}

// ───────────────────────────── Rutas ─────────────────────────────

const esquemaRuta = z.object({
  codigo: z.string().trim().min(1, 'Captura el código (R01…).').max(10),
  nombre: z.string().trim().min(1, 'Captura el nombre.'),
  vendedorId: z.string(),
  vehiculoId: z.string(),
  activo: z.boolean(),
});
type DatosRuta = z.infer<typeof esquemaRuta>;

/** Usuarios con rol Vendedor. La lista de usuarios requiere `seguridad.usuarios`; sin él solo se muestra el actual. */
function useVendedores(actual?: { id: string; nombre: string } | null) {
  const { tienePermiso } = useSesion();
  const puede = tienePermiso('seguridad.usuarios');
  const { data } = useListarUsuarios({ tamano: 200 }, { query: { enabled: puede } });
  const opciones = (data?.items ?? []).filter((u) => u.activo && u.roles.includes('Vendedor')).map((u) => ({ valor: u.id, etiqueta: u.nombreCompleto }));
  if (actual && !opciones.some((o) => o.valor === actual.id)) opciones.unshift({ valor: actual.id, etiqueta: actual.nombre });
  return { opciones, puede };
}

export function DialogoRuta({ ruta, abierto, onCambiar }: { ruta: RutaDto | null; abierto: boolean; onCambiar: (a: boolean) => void }) {
  const navegar = useNavigate();
  const crear = useCrearRuta();
  const actualizar = useActualizarRuta();
  const { data: vehiculos } = useListarVehiculos({ activo: true });
  const vendedores = useVendedores(ruta?.vendedorId ? { id: ruta.vendedorId, nombre: ruta.vendedorNombre ?? 'Vendedor actual' } : null);
  const [error, setErrorGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState } = useForm<DatosRuta>({
    resolver: zodResolver(esquemaRuta),
    values: ruta
      ? { codigo: ruta.codigo, nombre: ruta.nombre, vendedorId: ruta.vendedorId ?? '', vehiculoId: ruta.vehiculoId ?? '', activo: ruta.activo }
      : { codigo: '', nombre: '', vendedorId: '', vehiculoId: '', activo: true },
  });
  const enviar = handleSubmit(async (d) => {
    setErrorGeneral(null);
    const datos = { ...d, codigo: d.codigo.toUpperCase(), vendedorId: d.vendedorId || null, vehiculoId: d.vehiculoId || null };
    try {
      if (ruta) {
        await actualizar.mutateAsync({ id: ruta.id, data: datos });
        toast.success('Ruta actualizada.');
        onCambiar(false);
      } else {
        const creada = await crear.mutateAsync({ data: datos });
        toast.success(`Ruta ${creada.codigo} creada con su almacén ${creada.almacenCodigo} y su cliente "Público en general".`);
        onCambiar(false);
        await navegar({ to: '/logistica/rutas/$id', params: { id: creada.id } });
      }
    } catch (e) {
      setErrorGeneral(aplicarErroresApi<DatosRuta>(e, setError));
    }
  });
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo={ruta ? `Editar ruta ${ruta.codigo}` : 'Nueva ruta'}
      descripcion={ruta ? undefined : 'Al crearla se generan su almacén de ruta y su cliente genérico (RN-LOG-01, RN-VEN-10).'}
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
        {error && (
          <Aviso tipo="error" className="sm:col-span-2">
            {error}
          </Aviso>
        )}
        <Campo
          etiqueta="Código"
          disabled={!!ruta}
          ayuda={ruta ? 'El código no se cambia: se usa en los folios.' : 'Se usa en los folios de la app (R01-D03-000123).'}
          error={formState.errors.codigo?.message}
          {...register('codigo')}
        />
        <Campo etiqueta="Nombre" error={formState.errors.nombre?.message} {...register('nombre')} />
        <Selector
          etiqueta="Vendedor"
          vacio="Sin asignar"
          opciones={vendedores.opciones}
          ayuda={vendedores.puede ? 'Usuarios con rol Vendedor.' : 'Para elegir otro vendedor se requiere el permiso de usuarios.'}
          error={formState.errors.vendedorId?.message}
          {...register('vendedorId')}
        />
        <Selector
          etiqueta="Vehículo"
          vacio="Sin asignar"
          opciones={(vehiculos ?? []).map((v) => ({ valor: v.id, etiqueta: `${v.placas} · ${v.descripcion}` }))}
          {...register('vehiculoId')}
        />
        <Casilla etiqueta="Ruta activa" {...register('activo')} />
      </form>
    </Dialogo>
  );
}

export function Rutas() {
  const navegar = useNavigate();
  const { data, isLoading } = useListarRutas();
  const [dialogo, setDialogo] = useState(false);
  const columnas = useMemo<ColumnDef<RutaDto, unknown>[]>(
    () => [
      { header: 'Código', accessorKey: 'codigo', cell: ({ row }) => <span className="font-medium">{row.original.codigo}</span> },
      { header: 'Nombre', accessorKey: 'nombre' },
      { header: 'Vendedor', accessorKey: 'vendedorNombre', cell: ({ row }) => row.original.vendedorNombre ?? '—' },
      { header: 'Vehículo', accessorKey: 'vehiculoPlacas', cell: ({ row }) => row.original.vehiculoPlacas ?? '—', meta: { ocultarEnMovil: true } },
      { header: 'Almacén', accessorKey: 'almacenCodigo', meta: { ocultarEnMovil: true } },
      { header: 'Clientes', accessorKey: 'clientes', meta: { alinear: 'fin' } },
      { header: 'Estado', accessorKey: 'activo', cell: ({ row }) => <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} /> },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Rutas"
        subtitulo="Rutas de reparto con su vendedor, vehículo, almacén y clientes en orden de visita."
        acciones={
          <Boton icono="add" onClick={() => setDialogo(true)}>
            Nueva ruta
          </Boton>
        }
      />
      <TablaDatos
        columnas={columnas}
        datos={data}
        cargando={isLoading}
        idFila={(r) => r.id}
        alClicFila={(r) => navegar({ to: '/logistica/rutas/$id', params: { id: r.id } })}
        mensajeVacio="No hay rutas registradas."
      />
      <DialogoRuta ruta={null} abierto={dialogo} onCambiar={setDialogo} />
    </Pagina>
  );
}
