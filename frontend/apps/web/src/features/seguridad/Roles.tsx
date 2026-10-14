import { mensajeError, useActualizarPermisosRol, useCrearRol, useListarPermisos, useListarRoles, type RolDto } from '@tg/api-client';
import { AreaTexto, Aviso, BarraAccion, Boton, Campo, Casilla, Dialogo, Tarjeta, cn, toast } from '@tg/ui';
import { useEffect, useMemo, useState } from 'react';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { agruparPermisos, alternarGrupo } from './permisos';

function DialogoNuevoRol({ abierto, onCambiar, alCrear }: { abierto: boolean; onCambiar: (a: boolean) => void; alCrear: (rol: RolDto) => void }) {
  const crear = useCrearRol();
  const [nombre, setNombre] = useState('');
  const [descripcion, setDescripcion] = useState('');
  const [error, setError] = useState<string | null>(null);
  const guardar = async () => {
    setError(null);
    if (!nombre.trim()) {
      setError('Escribe el nombre del rol.');
      return;
    }
    try {
      const rol = await crear.mutateAsync({ data: { nombre: nombre.trim(), descripcion: descripcion.trim() || null, permisos: [] } });
      toast.success(`Rol ${rol.nombre} creado; ahora elige sus permisos.`);
      setNombre('');
      setDescripcion('');
      onCambiar(false);
      alCrear(rol);
    } catch (e) {
      setError(mensajeError(e));
    }
  };
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo="Nuevo rol"
      ancho="chico"
      acciones={
        <>
          <Boton variante="texto" onClick={() => onCambiar(false)}>
            Cancelar
          </Boton>
          <Boton cargando={crear.isPending} onClick={guardar}>
            Crear rol
          </Boton>
        </>
      }
    >
      <div className="grid gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Campo etiqueta="Nombre" value={nombre} onChange={(e) => setNombre(e.target.value)} />
        <AreaTexto etiqueta="Descripción (opcional)" value={descripcion} onChange={(e) => setDescripcion(e.target.value)} />
      </div>
    </Dialogo>
  );
}

/** Roles como agrupaciones de permisos (RN-SEG-04): el backend autoriza por permiso, no por rol. */
export function Roles() {
  const roles = useListarRoles();
  const permisos = useListarPermisos();
  const actualizar = useActualizarPermisosRol();
  const [rolId, setRolId] = useState<string | null>(null);
  const [seleccion, setSeleccion] = useState<string[]>([]);
  const [nuevo, setNuevo] = useState(false);
  const grupos = useMemo(() => agruparPermisos(permisos.data ?? []), [permisos.data]);
  const rol = roles.data?.find((r) => r.id === rolId) ?? roles.data?.[0] ?? null;

  useEffect(() => {
    setSeleccion(rol?.permisos ?? []);
  }, [rol?.id, rol?.permisos]);

  const cambiado = rol ? [...seleccion].sort().join() !== [...rol.permisos].sort().join() : false;

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Roles y permisos"
        subtitulo="Cada permiso habilita pantallas y acciones; los cambios aplican en el siguiente inicio de sesión o refresco de token."
        acciones={
          <Boton icono="add" onClick={() => setNuevo(true)}>
            Nuevo rol
          </Boton>
        }
      />
      <EstadoConsulta cargando={roles.isLoading || permisos.isLoading} error={roles.error ?? permisos.error}>
        <div className="grid gap-4 lg:grid-cols-[280px_minmax(0,1fr)]">
          <Tarjeta className="p-2 sm:p-2">
            <ul className="m-0 flex list-none flex-col gap-1 p-0" aria-label="Roles">
              {(roles.data ?? []).map((r) => (
                <li key={r.id}>
                  <button
                    type="button"
                    aria-current={rol?.id === r.id ? 'true' : undefined}
                    onClick={() => setRolId(r.id)}
                    className={cn(
                      'flex w-full items-center justify-between gap-2 rounded-xl px-3 py-2.5 text-left hover:bg-row-hover',
                      rol?.id === r.id && 'bg-soft font-medium text-primary',
                    )}
                  >
                    <span>{r.nombre}</span>
                    <span className="text-xs text-muted">
                      {r.permisos.length} permisos · {r.usuarios} usuario(s)
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </Tarjeta>
          {rol && (
            <div className="flex min-w-0 flex-col gap-4">
              {grupos.map((g) => {
                const marcados = g.permisos.filter((p) => seleccion.includes(p.clave)).length;
                return (
                  <Tarjeta
                    key={g.modulo}
                    titulo={g.etiqueta}
                    subtitulo={`${marcados} de ${g.permisos.length}`}
                    acciones={
                      <Boton variante="texto" tamano="chico" onClick={() => setSeleccion((s) => alternarGrupo(s, g, marcados < g.permisos.length))}>
                        {marcados < g.permisos.length ? 'Marcar todos' : 'Quitar todos'}
                      </Boton>
                    }
                  >
                    <div className="grid gap-1 sm:grid-cols-2">
                      {g.permisos.map((p) => (
                        <Casilla
                          key={p.clave}
                          etiqueta={p.descripcion}
                          descripcion={p.clave}
                          checked={seleccion.includes(p.clave)}
                          onChange={(e) => setSeleccion((s) => (e.target.checked ? [...s, p.clave] : s.filter((c) => c !== p.clave)))}
                        />
                      ))}
                    </div>
                  </Tarjeta>
                );
              })}
              <BarraAccion resumen={`${rol.nombre}: ${seleccion.length} permisos${cambiado ? ' · cambios sin guardar' : ''}`}>
                <Boton variante="texto" disabled={!cambiado} onClick={() => setSeleccion(rol.permisos)}>
                  Descartar
                </Boton>
                <Boton
                  disabled={!cambiado}
                  cargando={actualizar.isPending}
                  onClick={async () => {
                    try {
                      await actualizar.mutateAsync({ id: rol.id, data: { permisos: seleccion } });
                      toast.success(`Permisos de ${rol.nombre} guardados.`);
                    } catch (e) {
                      toast.error(mensajeError(e));
                    }
                  }}
                >
                  Guardar permisos
                </Boton>
              </BarraAccion>
            </div>
          )}
        </div>
      </EstadoConsulta>
      <DialogoNuevoRol abierto={nuevo} onCambiar={setNuevo} alCrear={(r) => setRolId(r.id)} />
    </Pagina>
  );
}
