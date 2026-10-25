import { zodResolver } from '@hookform/resolvers/zod';
import {
  esErrorApi,
  mensajeError,
  useActualizarUsuario,
  useCrearUsuario,
  useDesbloquearUsuario,
  useListarRoles,
  useListarUsuarios,
  type UsuarioDto,
} from '@tg/api-client';
import { formatoFechaHora } from '@tg/shared';
import { Aviso, Boton, BotonIcono, Campo, Casilla, Dialogo, DialogoConfirmacion, EtiquetaEstado, MenuAcciones, TablaDatos, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { Controller, useForm, type Path } from 'react-hook-form';
import { z } from 'zod';
import { BarraFiltros } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';

const esquema = (nuevo: boolean) =>
  z.object({
    usuario: z
      .string()
      .trim()
      .min(1, 'Captura el usuario.')
      .max(64)
      .regex(/^[a-zA-Z0-9._-]+$/, 'Usa solo letras, números, punto, guion o guion bajo.'),
    nombreCompleto: z.string().trim().min(1, 'Escribe el nombre completo.'),
    email: z.union([z.literal(''), z.string().trim().email('El correo no es válido.')]),
    activo: z.boolean(),
    roles: z.array(z.string()).min(1, 'Asigna al menos un rol.'),
    contrasena: nuevo
      ? z.string().min(8, 'Mínimo 8 caracteres.').regex(/\d/, 'Debe incluir un número.')
      : z.union([z.literal(''), z.string().min(8, 'Mínimo 8 caracteres.').regex(/\d/, 'Debe incluir un número.')]),
  });
type DatosUsuario = z.infer<ReturnType<typeof esquema>>;

function DialogoUsuario({ usuario, abierto, onCambiar }: { usuario: UsuarioDto | null; abierto: boolean; onCambiar: (a: boolean) => void }) {
  const { data: roles } = useListarRoles();
  const crear = useCrearUsuario();
  const actualizar = useActualizarUsuario();
  const [error, setErrorGeneral] = useState<string | null>(null);
  const { register, control, handleSubmit, setError, formState } = useForm<DatosUsuario>({
    resolver: zodResolver(esquema(!usuario)),
    values: usuario
      ? { usuario: usuario.usuario, nombreCompleto: usuario.nombreCompleto, email: usuario.email ?? '', activo: usuario.activo, roles: usuario.roles, contrasena: '' }
      : { usuario: '', nombreCompleto: '', email: '', activo: true, roles: [], contrasena: '' },
  });
  const enviar = handleSubmit(async (d) => {
    setErrorGeneral(null);
    const datos = { ...d, email: d.email || null, contrasena: d.contrasena || null };
    try {
      if (usuario) await actualizar.mutateAsync({ id: usuario.id, data: datos });
      else await crear.mutateAsync({ data: datos });
      toast.success('Usuario guardado.');
      onCambiar(false);
    } catch (e) {
      if (esErrorApi(e)) Object.entries(e.errores).forEach(([c, m]) => setError(c as Path<DatosUsuario>, { message: m[0] }));
      setErrorGeneral(mensajeError(e));
    }
  });
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo={usuario ? `Editar ${usuario.usuario}` : 'Nuevo usuario'}
      ancho="normal"
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
        <Campo etiqueta="Usuario" autoComplete="off" error={formState.errors.usuario?.message} {...register('usuario')} />
        <Campo etiqueta="Nombre completo" error={formState.errors.nombreCompleto?.message} {...register('nombreCompleto')} />
        <Campo etiqueta="Correo (opcional)" type="email" error={formState.errors.email?.message} {...register('email')} />
        <Campo
          etiqueta={usuario ? 'Nueva contraseña (opcional)' : 'Contraseña inicial'}
          type="password"
          autoComplete="new-password"
          ayuda={usuario ? 'Déjala vacía para no cambiarla; cambiarla cierra sus sesiones.' : 'Mínimo 8 caracteres con un número.'}
          error={formState.errors.contrasena?.message}
          {...register('contrasena')}
        />
        <Casilla etiqueta="Activo" descripcion="Un usuario inactivo no puede iniciar sesión." {...register('activo')} />
        <fieldset className="m-0 border-0 p-0 sm:col-span-2">
          <legend className="mb-1 text-sm font-medium">Roles</legend>
          <Controller
            control={control}
            name="roles"
            render={({ field }) => (
              <div className="grid grid-cols-2 gap-1 sm:grid-cols-3">
                {(roles ?? []).map((r) => (
                  <Casilla
                    key={r.id}
                    etiqueta={r.nombre}
                    checked={field.value.includes(r.nombre)}
                    onChange={(e) => field.onChange(e.target.checked ? [...field.value, r.nombre] : field.value.filter((x) => x !== r.nombre))}
                  />
                ))}
              </div>
            )}
          />
          {formState.errors.roles && <p className="m-0 mt-1 text-xs text-error">{formState.errors.roles.message}</p>}
        </fieldset>
      </form>
    </Dialogo>
  );
}

export function Usuarios() {
  const [buscar, setBuscar] = useState('');
  const [pagina, setPagina] = useState(1);
  const { data, isLoading, isFetching } = useListarUsuarios({ buscar: buscar || undefined, pagina, tamano: 50 });
  const desbloquear = useDesbloquearUsuario();
  const [dialogo, setDialogo] = useState<{ abierto: boolean; usuario: UsuarioDto | null }>({ abierto: false, usuario: null });
  const [porDesbloquear, setPorDesbloquear] = useState<UsuarioDto | null>(null);

  const columnas = useMemo<ColumnDef<UsuarioDto, unknown>[]>(
    () => [
      { header: 'Usuario', accessorKey: 'usuario', cell: ({ row }) => <span className="font-medium">{row.original.usuario}</span> },
      { header: 'Nombre', accessorKey: 'nombreCompleto' },
      { header: 'Correo', accessorKey: 'email', cell: ({ row }) => row.original.email ?? '—', meta: { ocultarEnMovil: true } },
      { header: 'Roles', accessorKey: 'roles', cell: ({ row }) => row.original.roles.join(', ') || '—' },
      {
        header: 'Estado',
        id: 'estado',
        cell: ({ row }) =>
          row.original.bloqueadoHasta ? (
            <span title={`Bloqueado hasta ${formatoFechaHora(row.original.bloqueadoHasta)}`}>
              <EtiquetaEstado etiqueta="Bloqueado" color="naranja" />
            </span>
          ) : (
            <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} />
          ),
      },
      {
        id: 'acciones',
        header: '',
        enableSorting: false,
        meta: { alinear: 'fin' },
        cell: ({ row }) => (
          <MenuAcciones
            disparador={<BotonIcono icono="more_vert" etiqueta={`Acciones de ${row.original.usuario}`} plano />}
            opciones={[
              { etiqueta: 'Editar', icono: 'edit', alSeleccionar: () => setDialogo({ abierto: true, usuario: row.original }) },
              {
                etiqueta: 'Desbloquear',
                icono: 'lock_open',
                deshabilitada: !row.original.bloqueadoHasta,
                alSeleccionar: () => setPorDesbloquear(row.original),
              },
            ]}
          />
        ),
      },
    ],
    [],
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Usuarios"
        subtitulo="Cuentas de acceso a la web y a la app de ruta; tras 5 intentos fallidos se bloquean 15 minutos (RN-SEG-05)."
        acciones={
          <Boton icono="person_add" onClick={() => setDialogo({ abierto: true, usuario: null })}>
            Nuevo usuario
          </Boton>
        }
      />
      <BarraFiltros>
        <Campo
          etiqueta="Buscar por usuario o nombre"
          type="search"
          value={buscar}
          onChange={(e) => {
            setBuscar(e.target.value);
            setPagina(1);
          }}
        />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(u) => u.id}
        alClicFila={(u) => setDialogo({ abierto: true, usuario: u })}
        mensajeVacio="No hay usuarios con esa búsqueda."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
      {dialogo.abierto && <DialogoUsuario {...dialogo} onCambiar={(a) => setDialogo((d) => ({ ...d, abierto: a }))} />}
      {porDesbloquear && (
        <DialogoConfirmacion
          abierto
          onCambiar={(a) => !a && setPorDesbloquear(null)}
          titulo="Desbloquear usuario"
          mensaje={`${porDesbloquear.nombreCompleto} podrá volver a iniciar sesión de inmediato.`}
          textoConfirmar="Desbloquear"
          alConfirmar={async () => {
            await desbloquear.mutateAsync({ id: porDesbloquear.id });
            toast.success('Usuario desbloqueado.');
          }}
        />
      )}
    </Pagina>
  );
}
