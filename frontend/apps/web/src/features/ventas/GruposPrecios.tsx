import { zodResolver } from '@hookform/resolvers/zod';
import {
  esErrorApi,
  mensajeError,
  useActualizarGrupoCliente,
  useCrearGrupoCliente,
  useGuardarPreciosCliente,
  useGuardarProductoExclusivos,
  useListarCanales,
  useListarGruposClientes,
  useListarListasPrecio,
  useListarPreciosCliente,
  useListarProductoExclusivos,
  type GrupoClienteDto,
  type ProductoDto,
} from '@tg/api-client';
import { fechaOperativa, formatoFecha, formatoMoneda } from '@tg/shared';
import { Aviso, BarraAccion, Boton, BotonIcono, Campo, Casilla, Dialogo, EtiquetaEstado, Selector, TablaDatos, Tarjeta, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { useForm, type Path } from 'react-hook-form';
import { z } from 'zod';
import { SelectorProducto } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { unidadSugerida, unidadesVenta } from './partidas';
import { SelectorCliente } from './selectores';

let consecutivo = 0;
const nuevaClave = () => `g${++consecutivo}`;

// ───────────────────────────── Grupos de clientes ─────────────────────────────

const esquemaGrupo = z.object({
  clave: z.string().trim().min(1, 'Captura la clave.').max(20),
  nombre: z.string().trim().min(1, 'Captura el nombre.'),
  canalId: z.string().min(1, 'Elige el canal.'),
  listaPrecioId: z.string(),
  activo: z.boolean(),
});
type DatosGrupo = z.output<typeof esquemaGrupo>;

function DialogoGrupo({ grupo, abierto, onCambiar }: { grupo: GrupoClienteDto | null; abierto: boolean; onCambiar: (a: boolean) => void }) {
  const { data: canales } = useListarCanales();
  const { data: listas } = useListarListasPrecio();
  const crear = useCrearGrupoCliente();
  const actualizar = useActualizarGrupoCliente();
  const [error, setErrorGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState } = useForm<z.input<typeof esquemaGrupo>, unknown, DatosGrupo>({
    resolver: zodResolver(esquemaGrupo),
    values: grupo
      ? { clave: grupo.clave, nombre: grupo.nombre, canalId: grupo.canalId, listaPrecioId: grupo.listaPrecioId ?? '', activo: grupo.activo }
      : { clave: '', nombre: '', canalId: '', listaPrecioId: '', activo: true },
  });
  const enviar = handleSubmit(async (d) => {
    setErrorGeneral(null);
    try {
      const data = { ...d, listaPrecioId: d.listaPrecioId || null };
      if (grupo) await actualizar.mutateAsync({ id: grupo.id, data });
      else await crear.mutateAsync({ data });
      toast.success('Grupo guardado.');
      onCambiar(false);
    } catch (e) {
      if (esErrorApi(e)) Object.entries(e.errores).forEach(([campo, m]) => setError(campo as Path<DatosGrupo>, { message: m[0] }));
      setErrorGeneral(mensajeError(e));
    }
  });
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo={grupo ? `Editar ${grupo.clave}` : 'Nuevo grupo'}
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
          etiqueta="Canal"
          vacio="Elige el canal"
          opciones={(canales ?? []).map((c) => ({ valor: c.id, etiqueta: `${c.clave} · ${c.nombre}` }))}
          error={formState.errors.canalId?.message}
          {...register('canalId')}
        />
        <Selector
          etiqueta="Lista de precios"
          vacio="Sin lista"
          opciones={(listas ?? []).map((l) => ({ valor: l.id, etiqueta: l.nombre }))}
          ayuda="Los clientes nuevos del grupo la heredan."
          {...register('listaPrecioId')}
        />
        <Casilla etiqueta="Activo" {...register('activo')} />
      </form>
    </Dialogo>
  );
}

export function GruposClientes() {
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('ventas.configurar');
  const { data, isLoading } = useListarGruposClientes();
  const [dialogo, setDialogo] = useState<{ abierto: boolean; grupo: GrupoClienteDto | null }>({ abierto: false, grupo: null });
  const columnas = useMemo<ColumnDef<GrupoClienteDto, unknown>[]>(
    () => [
      { header: 'Clave', accessorKey: 'clave', cell: ({ row }) => <span className="font-medium">{row.original.clave}</span> },
      { header: 'Nombre', accessorKey: 'nombre' },
      { header: 'Canal', accessorKey: 'canal' },
      { header: 'Lista de precios', accessorKey: 'listaPrecio', cell: ({ row }) => row.original.listaPrecio ?? '—' },
      { header: 'Clientes', accessorKey: 'clientes', meta: { alinear: 'fin' } },
      { header: 'Estado', accessorKey: 'activo', cell: ({ row }) => <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} /> },
      ...(editable
        ? ([
            {
              id: 'acciones',
              header: '',
              enableSorting: false,
              meta: { alinear: 'fin' },
              cell: ({ row }) => (
                <BotonIcono icono="edit" etiqueta={`Editar ${row.original.clave}`} plano onClick={() => setDialogo({ abierto: true, grupo: row.original })} />
              ),
            },
          ] as ColumnDef<GrupoClienteDto, unknown>[])
        : []),
    ],
    [editable],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Grupos de clientes"
        subtitulo="Cadenas o clientes relacionados: comparten lista de precios y productos exclusivos (RN-VEN-12, RN-VEN-14)."
        migas={[{ etiqueta: 'Ventas' }, { etiqueta: 'Grupos de clientes' }]}
        acciones={editable && <Boton icono="add" onClick={() => setDialogo({ abierto: true, grupo: null })}>Nuevo grupo</Boton>}
      />
      <TablaDatos columnas={columnas} datos={data} cargando={isLoading} idFila={(g) => g.id} />
      <DialogoGrupo {...dialogo} onCambiar={(a) => setDialogo((d) => ({ ...d, abierto: a }))} />
    </Pagina>
  );
}

// ───────────────────────────── Precios especiales ─────────────────────────────

interface FilaEspecial {
  clave: string;
  producto: Pick<ProductoDto, 'id' | 'sku' | 'nombre'> | null;
  unidadId: string;
  opcionesUnidad: { valor: string; etiqueta: string }[];
  precio: string;
  vigenteDesde: string;
  precioLista: number | null;
}

/** Precios especiales del cliente (RN-VEN-12): ganan sobre su lista desde la fecha de vigencia. */
export function PreciosEspecialesCliente({ clienteId }: { clienteId: string }) {
  const consulta = useListarPreciosCliente(clienteId);
  return (
    <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
      {consulta.data && (
        <EditorEspeciales
          key={consulta.dataUpdatedAt}
          clienteId={clienteId}
          inicial={consulta.data.map((p) => ({
            clave: nuevaClave(),
            producto: { id: p.productoId, sku: p.sku, nombre: p.producto },
            unidadId: p.unidadId,
            opcionesUnidad: [{ valor: p.unidadId, etiqueta: p.unidad }],
            precio: String(p.precioUnitario),
            vigenteDesde: p.vigenteDesde,
            precioLista: p.precioLista ?? null,
          }))}
        />
      )}
    </EstadoConsulta>
  );
}

function EditorEspeciales({ clienteId, inicial }: { clienteId: string; inicial: FilaEspecial[] }) {
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('ventas.configurar');
  const guardar = useGuardarPreciosCliente();
  const [filas, setFilas] = useState(inicial);
  const [error, setError] = useState<string | null>(null);
  const cambiar = (clave: string, cambio: Partial<FilaEspecial>) => setFilas((fs) => fs.map((f) => (f.clave === clave ? { ...f, ...cambio } : f)));
  const duplicadas = new Set(
    filas.filter((f, i) => filas.findIndex((g) => g.producto?.id === f.producto?.id && g.unidadId === f.unidadId) !== i).map((f) => f.clave),
  );
  const invalidas = filas.some((f) => !f.producto || !f.unidadId || f.precio === '' || !(Number(f.precio) >= 0) || !f.vigenteDesde) || duplicadas.size > 0;

  return (
    <div className="flex flex-col gap-4">
      <Tarjeta
        titulo="Precios especiales"
        subtitulo="Precio sin impuestos que gana sobre la lista desde su fecha de vigencia. Quitar un renglón lo desactiva."
        acciones={
          editable && (
            <Boton
              variante="secundario"
              tamano="chico"
              icono="add"
              onClick={() =>
                setFilas((fs) => [
                  ...fs,
                  { clave: nuevaClave(), producto: null, unidadId: '', opcionesUnidad: [], precio: '', vigenteDesde: fechaOperativa(), precioLista: null },
                ])
              }
            >
              Agregar precio
            </Boton>
          )
        }
      >
        {error && <Aviso tipo="error" className="mb-3">{error}</Aviso>}
        <div className="flex flex-col gap-2">
          {filas.length === 0 && <p className="m-0 text-sm text-muted">Sin precios especiales: se usa la lista del cliente.</p>}
          {filas.map((f) => (
            <div key={f.clave} className="grid items-start gap-2 rounded-xl border border-row-divider p-3 sm:grid-cols-[minmax(0,1fr)_150px_170px_170px_auto]">
              <SelectorProducto
                compacto
                valor={f.producto?.id ?? null}
                producto={f.producto}
                filtro={{ esVendible: true }}
                deshabilitado={!editable}
                onCambiar={(p) =>
                  cambiar(f.clave, {
                    producto: p,
                    opcionesUnidad: unidadesVenta(p),
                    unidadId: p ? unidadSugerida(p) : '',
                    precioLista: null,
                  })
                }
              />
              <Selector
                compacto
                etiqueta="Unidad"
                opciones={f.opcionesUnidad}
                vacio={f.opcionesUnidad.length ? undefined : 'Unidad'}
                value={f.unidadId}
                disabled={!editable || !f.producto}
                onChange={(e) => cambiar(f.clave, { unidadId: e.target.value })}
              />
              <Campo
                compacto
                etiqueta="Precio especial"
                type="number"
                step="any"
                min="0"
                inputMode="decimal"
                prefijo="$"
                value={f.precio}
                disabled={!editable}
                ayuda={f.precioLista != null ? `Lista: ${formatoMoneda(f.precioLista)}` : undefined}
                onChange={(e) => cambiar(f.clave, { precio: e.target.value })}
              />
              <Campo
                compacto
                etiqueta="Vigente desde"
                type="date"
                value={f.vigenteDesde}
                disabled={!editable}
                ayuda={!editable ? formatoFecha(f.vigenteDesde) : undefined}
                onChange={(e) => cambiar(f.clave, { vigenteDesde: e.target.value })}
              />
              {editable && <BotonIcono icono="delete" etiqueta="Quitar precio" onClick={() => setFilas((fs) => fs.filter((x) => x.clave !== f.clave))} />}
              {duplicadas.has(f.clave) && <p className="m-0 text-xs text-error sm:col-span-5">Producto y unidad repetidos.</p>}
            </div>
          ))}
        </div>
      </Tarjeta>
      {editable && (
        <BarraAccion resumen={invalidas ? 'Completa producto, unidad, precio y fecha en cada renglón.' : `${filas.length} precios especiales`}>
          <Boton
            disabled={invalidas}
            cargando={guardar.isPending}
            onClick={async () => {
              setError(null);
              try {
                await guardar.mutateAsync({
                  id: clienteId,
                  data: {
                    precios: filas.map((f) => ({
                      productoId: f.producto!.id,
                      unidadId: f.unidadId,
                      precioUnitario: Number(f.precio),
                      vigenteDesde: f.vigenteDesde,
                    })),
                  },
                });
                toast.success('Precios especiales guardados.');
              } catch (e) {
                setError(mensajeError(e));
              }
            }}
          >
            Guardar precios
          </Boton>
        </BarraAccion>
      )}
    </div>
  );
}

// ───────────────────────────── Productos exclusivos ─────────────────────────────

interface FilaExclusivo {
  clave: string;
  clienteId: string | null;
  cliente: string | null;
  grupoClienteId: string | null;
}

/** Clientes y grupos a los que se limita la venta del producto (RN-VEN-14). Sin renglones, se vende a todos. */
export function ExclusividadProducto({ productoId }: { productoId: string }) {
  const consulta = useListarProductoExclusivos(productoId);
  const grupos = useListarGruposClientes({ activo: true });
  return (
    <EstadoConsulta cargando={consulta.isLoading || grupos.isLoading} error={consulta.error ?? grupos.error}>
      {consulta.data && (
        <EditorExclusivos
          key={consulta.dataUpdatedAt}
          productoId={productoId}
          grupos={grupos.data ?? []}
          inicial={consulta.data.map((x) => ({
            clave: nuevaClave(),
            clienteId: x.clienteId ?? null,
            cliente: x.cliente ?? null,
            grupoClienteId: x.grupoClienteId ?? null,
          }))}
        />
      )}
    </EstadoConsulta>
  );
}

function EditorExclusivos({ productoId, grupos, inicial }: { productoId: string; grupos: GrupoClienteDto[]; inicial: FilaExclusivo[] }) {
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('ventas.configurar');
  const guardar = useGuardarProductoExclusivos();
  const [filas, setFilas] = useState(inicial);
  const [error, setError] = useState<string | null>(null);
  const cambiar = (clave: string, cambio: Partial<FilaExclusivo>) => setFilas((fs) => fs.map((f) => (f.clave === clave ? { ...f, ...cambio } : f)));
  const invalidas = filas.some((f) => !f.clienteId && !f.grupoClienteId);
  const agregar = (tipo: 'cliente' | 'grupo') =>
    setFilas((fs) => [...fs, { clave: nuevaClave(), clienteId: null, cliente: tipo === 'cliente' ? '' : null, grupoClienteId: null }]);

  return (
    <div className="flex flex-col gap-4">
      <Tarjeta
        titulo="Exclusividad"
        subtitulo="Si hay renglones, el producto solo se vende a estos clientes o a los clientes de estos grupos. En la app de ruta se oculta a los demás."
        acciones={
          editable && (
            <div className="flex gap-2">
              <Boton variante="secundario" tamano="chico" icono="person_add" onClick={() => agregar('cliente')}>
                Cliente
              </Boton>
              <Boton variante="secundario" tamano="chico" icono="group_add" onClick={() => agregar('grupo')}>
                Grupo
              </Boton>
            </div>
          )
        }
      >
        {error && <Aviso tipo="error" className="mb-3">{error}</Aviso>}
        <div className="flex flex-col gap-2">
          {filas.length === 0 && <p className="m-0 text-sm text-muted">Sin exclusividad: se vende a todos los clientes.</p>}
          {filas.map((f) => (
            <div key={f.clave} className="grid items-start gap-2 rounded-xl border border-row-divider p-3 sm:grid-cols-[minmax(0,1fr)_auto]">
              {f.cliente != null ? (
                <SelectorCliente
                  valor={f.clienteId}
                  cliente={f.clienteId ? { id: f.clienteId, codigo: '', nombre: f.cliente } : null}
                  deshabilitado={!editable}
                  onCambiar={(c) => cambiar(f.clave, { clienteId: c?.id ?? null, cliente: c ? `${c.codigo} · ${c.nombre}` : '' })}
                />
              ) : (
                <Selector
                  etiqueta="Grupo"
                  vacio="Elige el grupo"
                  opciones={grupos.map((g) => ({ valor: g.id, etiqueta: `${g.clave} · ${g.nombre}` }))}
                  value={f.grupoClienteId ?? ''}
                  disabled={!editable}
                  onChange={(e) => cambiar(f.clave, { grupoClienteId: e.target.value || null })}
                />
              )}
              {editable && <BotonIcono icono="delete" etiqueta="Quitar" onClick={() => setFilas((fs) => fs.filter((x) => x.clave !== f.clave))} />}
            </div>
          ))}
        </div>
      </Tarjeta>
      {editable && (
        <BarraAccion resumen={invalidas ? 'Elige el cliente o el grupo en cada renglón.' : `${filas.length} renglones`}>
          <Boton
            disabled={invalidas}
            cargando={guardar.isPending}
            onClick={async () => {
              setError(null);
              try {
                await guardar.mutateAsync({
                  productoId,
                  data: { exclusivos: filas.map((f) => ({ clienteId: f.clienteId, grupoClienteId: f.clienteId ? null : f.grupoClienteId })) },
                });
                toast.success('Exclusividad guardada.');
              } catch (e) {
                setError(mensajeError(e));
              }
            }}
          >
            Guardar exclusividad
          </Boton>
        </BarraAccion>
      )}
    </div>
  );
}
