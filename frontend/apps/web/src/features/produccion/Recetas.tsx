import { useNavigate, useParams } from '@tanstack/react-router';
import {
  esErrorApi,
  mensajeError,
  obtenerProducto,
  useActualizarReceta,
  useCrearReceta,
  useListarRecetas,
  useNuevaVersionReceta,
  useObtenerReceta,
  usePublicarReceta,
  type ComponenteDto,
  type EstadoReceta,
  type ProductoDto,
  type RecetaDto,
} from '@tg/api-client';
import { formatoCantidad, formatoFechaHora, formatoPorcentaje } from '@tg/shared';
import {
  Aviso,
  BarraAccion,
  Boton,
  BotonIcono,
  Campo,
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
import { useEffect, useMemo, useState } from 'react';
import { BarraFiltros, SelectorProducto } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { unidadesProduccion } from './calculos';

const ESTADOS = [
  { valor: 'Borrador', etiqueta: 'Borrador' },
  { valor: 'Vigente', etiqueta: 'Vigente' },
  { valor: 'Obsoleta', etiqueta: 'Obsoleta' },
];

export function Recetas() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const [estado, setEstado] = useState('');
  const [producto, setProducto] = useState<ProductoDto | null>(null);
  const { data, isLoading, isFetching } = useListarRecetas({
    estado: (estado || undefined) as EstadoReceta | undefined,
    productoId: producto?.id,
  });
  const columnas = useMemo<ColumnDef<RecetaDto, unknown>[]>(
    () => [
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block font-medium">{row.original.producto}</span>
            <span className="text-xs text-muted">{row.original.sku}</span>
          </span>
        ),
      },
      { header: 'Receta', accessorKey: 'nombre' },
      { header: 'Versión', accessorKey: 'version', cell: ({ row }) => `v${row.original.version}`, meta: { alinear: 'centro' } },
      {
        header: 'Rendimiento',
        accessorKey: 'rendimientoCantidad',
        cell: ({ row }) => `${formatoCantidad(row.original.rendimientoCantidad)} ${row.original.unidadBase}`,
        meta: { alinear: 'fin' },
      },
      {
        header: 'Merma esperada',
        accessorKey: 'mermaEsperadaPct',
        cell: ({ row }) => formatoPorcentaje(row.original.mermaEsperadaPct),
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      { header: 'Componentes', id: 'componentes', cell: ({ row }) => row.original.componentes.length, meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Recetas"
        subtitulo="Cuánto producto sale y de qué componentes. Solo una versión vigente por producto (RN-PRO-01)."
        acciones={
          tienePermiso('produccion.recetas') && (
            <Boton icono="add" onClick={() => navegar({ to: '/produccion/recetas/nueva' })}>
              Nueva receta
            </Boton>
          )
        }
      />
      <BarraFiltros>
        <SelectorProducto etiqueta="Producto" valor={producto?.id} producto={producto} filtro={{ esProducible: true }} onCambiar={setProducto} />
        <Selector etiqueta="Estado" vacio="Todos" opciones={ESTADOS} value={estado} onChange={(e) => setEstado(e.target.value)} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(r) => r.id}
        alClicFila={(r) => navegar({ to: '/produccion/recetas/$id', params: { id: r.id } })}
        mensajeVacio="No hay recetas con esos filtros."
      />
    </Pagina>
  );
}

interface Componente {
  clave: string;
  producto: ProductoDto | null;
  unidadId: string;
  cantidad: string;
}

const nuevoComponente = (): Componente => ({ clave: crypto.randomUUID(), producto: null, unidadId: '', cantidad: '' });

/** Alta y edición de una receta en Borrador. */
function EditorReceta({ receta, productos }: { receta?: RecetaDto; productos: Record<string, ProductoDto> }) {
  const navegar = useNavigate();
  const crear = useCrearReceta();
  const actualizar = useActualizarReceta();
  const [producto, setProducto] = useState<ProductoDto | null>(receta ? (productos[receta.productoId] ?? null) : null);
  const [nombre, setNombre] = useState(receta?.nombre ?? '');
  const [rendimiento, setRendimiento] = useState(receta ? String(receta.rendimientoCantidad) : '');
  const [merma, setMerma] = useState(receta ? String(Math.round(receta.mermaEsperadaPct * 10000) / 100) : '0');
  const [notas, setNotas] = useState(receta?.notas ?? '');
  const [componentes, setComponentes] = useState<Componente[]>(
    receta?.componentes.map((c) => ({ clave: c.id, producto: productos[c.productoId] ?? null, unidadId: c.unidadId, cantidad: String(c.cantidad) })) ?? [
      nuevoComponente(),
    ],
  );
  const [errores, setErrores] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cambiar = (clave: string, c: Partial<Componente>) => setComponentes((cs) => cs.map((x) => (x.clave === clave ? { ...x, ...c } : x)));

  const guardar = async () => {
    setError(null);
    const capturados = componentes.filter((c) => c.producto);
    const e: Record<string, string> = {};
    if (!producto) e.productoId = 'Elige el producto que se obtiene.';
    if (!nombre.trim()) e.nombre = 'Captura el nombre.';
    if (!(Number(rendimiento) > 0)) e.rendimientoCantidad = 'El rendimiento debe ser mayor que cero.';
    if (!(Number(merma) >= 0 && Number(merma) < 100)) e.mermaEsperadaPct = 'Entre 0 y 100 %.';
    if (capturados.length === 0) e.componentes = 'Agrega al menos un componente.';
    capturados.forEach((c) => {
      if (!(Number(c.cantidad) > 0)) e[`${c.clave}.cantidad`] = 'Mayor que cero.';
      if (c.producto!.id === producto?.id) e[`${c.clave}.producto`] = 'Un producto no puede ser componente de sí mismo.';
    });
    setErrores(e);
    if (Object.keys(e).length) return;
    setGuardando(true);
    try {
      const datos = {
        productoId: producto!.id,
        nombre: nombre.trim(),
        rendimientoCantidad: Number(rendimiento),
        mermaEsperadaPct: Number(merma) / 100,
        notas: notas || null,
        componentes: capturados.map((c) => ({ productoId: c.producto!.id, unidadId: c.unidadId || null, cantidad: Number(c.cantidad) })),
      };
      const guardada = receta ? await actualizar.mutateAsync({ id: receta.id, data: datos }) : await crear.mutateAsync({ data: datos });
      toast.success(receta ? 'Receta actualizada.' : `Receta creada como borrador v${guardada.version}.`);
      if (!receta) await navegar({ to: '/produccion/recetas/$id', params: { id: guardada.id } });
    } catch (err) {
      if (esErrorApi(err)) setErrores(Object.fromEntries(Object.entries(err.errores).map(([c, m]) => [c, m[0]])));
      setError(mensajeError(err));
    } finally {
      setGuardando(false);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      {error && <Aviso tipo="error">{error}</Aviso>}
      <Tarjeta titulo="Producto y rendimiento">
        <div className="grid gap-4 sm:grid-cols-2">
          <SelectorProducto
            etiqueta="Producto que se obtiene"
            valor={producto?.id}
            producto={producto}
            filtro={{ esProducible: true }}
            onCambiar={(p) => {
              setProducto(p);
              if (p && !nombre) setNombre(p.nombre);
            }}
            error={errores.productoId}
            deshabilitado={!!receta}
          />
          <Campo etiqueta="Nombre de la receta" value={nombre} onChange={(e) => setNombre(e.target.value)} error={errores.nombre} />
          <Campo
            etiqueta={`Rendimiento (${producto?.unidadBaseClave ?? 'unidad base'})`}
            type="number"
            step="any"
            inputMode="decimal"
            value={rendimiento}
            onChange={(e) => setRendimiento(e.target.value)}
            error={errores.rendimientoCantidad}
            ayuda="Cuánto producto sale con las cantidades de abajo."
          />
          <Campo
            etiqueta="Merma esperada"
            type="number"
            step="any"
            inputMode="decimal"
            sufijo="%"
            value={merma}
            onChange={(e) => setMerma(e.target.value)}
            error={errores.mermaEsperadaPct}
          />
          <Campo etiqueta="Notas" className="sm:col-span-2" value={notas} onChange={(e) => setNotas(e.target.value)} />
        </div>
      </Tarjeta>
      <Tarjeta
        titulo="Componentes"
        subtitulo="Materias primas, empaques o semiterminados que se consumen. La masa se consume del inventario (multinivel por inventario)."
        acciones={
          <Boton variante="secundario" tamano="chico" icono="add" onClick={() => setComponentes((cs) => [...cs, nuevoComponente()])}>
            Agregar componente
          </Boton>
        }
      >
        {errores.componentes && <p className="m-0 mb-2 text-sm text-error">{errores.componentes}</p>}
        <div className="flex flex-col gap-3">
          {componentes.map((c, i) => (
            <div key={c.clave} className="grid items-start gap-3 rounded-xl border border-row-divider p-3 md:grid-cols-[2fr_1fr_1fr_auto]">
              <SelectorProducto
                etiqueta={`Componente ${i + 1}`}
                valor={c.producto?.id}
                producto={c.producto}
                onCambiar={(p) => cambiar(c.clave, { producto: p, unidadId: p?.unidadBaseId ?? '' })}
                error={errores[`${c.clave}.producto`]}
              />
              <Selector
                etiqueta="Unidad"
                opciones={c.producto ? unidadesProduccion(c.producto) : []}
                value={c.unidadId}
                disabled={!c.producto}
                onChange={(e) => cambiar(c.clave, { unidadId: e.target.value })}
              />
              <Campo
                etiqueta="Cantidad"
                type="number"
                step="any"
                inputMode="decimal"
                value={c.cantidad}
                onChange={(e) => cambiar(c.clave, { cantidad: e.target.value })}
                error={errores[`${c.clave}.cantidad`] ?? errores[`componentes[${i}].cantidad`]}
              />
              <BotonIcono
                icono="delete"
                etiqueta="Quitar componente"
                className="mt-2"
                disabled={componentes.length === 1}
                onClick={() => setComponentes((cs) => cs.filter((x) => x.clave !== c.clave))}
              />
            </div>
          ))}
        </div>
      </Tarjeta>
      <BarraAccion resumen="La receta queda en Borrador hasta que la publiques.">
        <Boton variante="texto" onClick={() => navegar({ to: '/produccion/recetas' })}>
          Cancelar
        </Boton>
        <Boton cargando={guardando} onClick={guardar}>
          {receta ? 'Guardar cambios' : 'Crear borrador'}
        </Boton>
      </BarraAccion>
    </div>
  );
}

export function RecetaNueva() {
  return (
    <Pagina ancho="medio">
      <EncabezadoPagina
        titulo="Nueva receta"
        subtitulo="Se crea como borrador versión 1 o como la siguiente versión del producto."
        migas={[{ etiqueta: 'Producción' }, { etiqueta: 'Recetas', a: '/produccion/recetas' }, { etiqueta: 'Nueva' }]}
      />
      <EditorReceta productos={{}} />
    </Pagina>
  );
}

export function RecetaDetalle() {
  const { id } = useParams({ strict: false }) as { id: string };
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const { data: receta, isLoading, error } = useObtenerReceta(id);
  const versiones = useListarRecetas({ productoId: receta?.productoId }, { query: { enabled: !!receta } });
  const publicar = usePublicarReceta();
  const nuevaVersion = useNuevaVersionReceta();
  const [confirmar, setConfirmar] = useState<'publicar' | 'version' | null>(null);
  const [productos, setProductos] = useState<Record<string, ProductoDto> | null>(null);
  const editable = tienePermiso('produccion.recetas') && receta?.estado === 'Borrador';

  // Para editar se necesitan las unidades de cada producto.
  useEffect(() => {
    if (!receta || receta.estado !== 'Borrador') return;
    const ids = [receta.productoId, ...receta.componentes.map((c) => c.productoId)];
    void Promise.all([...new Set(ids)].map((pid) => obtenerProducto(pid))).then((lista) =>
      setProductos(Object.fromEntries(lista.map((p) => [p.id, p]))),
    );
  }, [receta]);

  const columnas = useMemo<ColumnDef<ComponenteDto, unknown>[]>(
    () => [
      {
        header: 'Componente',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.producto}</span>
            <span className="text-xs text-muted">{row.original.sku}</span>
          </span>
        ),
      },
      { header: 'Cantidad', accessorKey: 'cantidad', cell: ({ row }) => `${formatoCantidad(row.original.cantidad)} ${row.original.unidad}`, meta: { alinear: 'fin' } },
      {
        header: 'En unidad base',
        accessorKey: 'cantidadBase',
        cell: ({ row }) => `${formatoCantidad(row.original.cantidadBase)} ${row.original.unidadBase}`,
        meta: { alinear: 'fin' },
      },
    ],
    [],
  );

  return (
    <Pagina ancho="medio">
      <EstadoConsulta cargando={isLoading} error={error}>
        {receta && (
          <>
            <EncabezadoPagina
              titulo={
                <span className="flex flex-wrap items-center gap-3">
                  {receta.nombre} v{receta.version} <EtiquetaEstado estado={receta.estado} />
                </span>
              }
              subtitulo={`${receta.sku} · ${receta.producto}`}
              migas={[{ etiqueta: 'Producción' }, { etiqueta: 'Recetas', a: '/produccion/recetas' }, { etiqueta: `${receta.sku} v${receta.version}` }]}
              acciones={
                tienePermiso('produccion.recetas') && (
                  <>
                    {receta.estado === 'Borrador' && (
                      <Boton icono="publish" onClick={() => setConfirmar('publicar')}>
                        Publicar
                      </Boton>
                    )}
                    {receta.estado !== 'Borrador' && (
                      <Boton variante="secundario" icono="content_copy" onClick={() => setConfirmar('version')}>
                        Nueva versión
                      </Boton>
                    )}
                  </>
                )
              }
            />
            {editable ? (
              productos ? (
                <EditorReceta key={receta.id} receta={receta} productos={productos} />
              ) : (
                <EstadoConsulta cargando>{null}</EstadoConsulta>
              )
            ) : (
              <div className="flex flex-col gap-4">
                {receta.estado !== 'Borrador' && (
                  <Aviso tipo="info">Una receta vigente u obsoleta no se edita: crea una nueva versión (RN-PRO-01).</Aviso>
                )}
                <Tarjeta>
                  <Datos>
                    <Dato etiqueta="Rendimiento">
                      {formatoCantidad(receta.rendimientoCantidad)} {receta.unidadBase}
                    </Dato>
                    <Dato etiqueta="Merma esperada">{formatoPorcentaje(receta.mermaEsperadaPct)}</Dato>
                    <Dato etiqueta="Vigente desde">{receta.vigenteDesde ? formatoFechaHora(receta.vigenteDesde) : '—'}</Dato>
                    <Dato etiqueta="Creada">{formatoFechaHora(receta.creadoEn)}</Dato>
                    {receta.notas && <Dato etiqueta="Notas">{receta.notas}</Dato>}
                  </Datos>
                </Tarjeta>
                <TablaDatos columnas={columnas} datos={receta.componentes} idFila={(c) => c.id} ordenable={false} />
              </div>
            )}
            <section className="mt-6">
              <h2 className="m-0 mb-2 text-base font-medium">Versiones de {receta.producto}</h2>
              <ul className="m-0 flex list-none flex-wrap gap-2 p-0">
                {(versiones.data ?? [])
                  .slice()
                  .sort((a, b) => b.version - a.version)
                  .map((v) => (
                    <li key={v.id}>
                      <button
                        type="button"
                        onClick={() => navegar({ to: '/produccion/recetas/$id', params: { id: v.id } })}
                        className={`flex items-center gap-2 rounded-full border px-3 py-1.5 text-sm ${v.id === receta.id ? 'border-primary text-primary' : 'border-outline-variant hover:bg-row-hover'}`}
                      >
                        v{v.version} <EtiquetaEstado estado={v.estado} />
                      </button>
                    </li>
                  ))}
              </ul>
            </section>
            <DialogoConfirmacion
              abierto={confirmar === 'publicar'}
              onCambiar={(a) => !a && setConfirmar(null)}
              titulo="Publicar receta"
              mensaje="Esta versión queda vigente y la anterior pasa a Obsoleta. Las órdenes ya liberadas conservan su versión."
              textoConfirmar="Publicar"
              alConfirmar={async () => {
                await publicar.mutateAsync({ id });
                toast.success(`Receta v${receta.version} vigente.`);
              }}
            />
            <DialogoConfirmacion
              abierto={confirmar === 'version'}
              onCambiar={(a) => !a && setConfirmar(null)}
              titulo="Crear nueva versión"
              mensaje="Se copia esta receta como borrador con el siguiente número de versión."
              textoConfirmar="Crear versión"
              alConfirmar={async () => {
                const nueva = await nuevaVersion.mutateAsync({ id });
                toast.success(`Borrador v${nueva.version} creado.`);
                await navegar({ to: '/produccion/recetas/$id', params: { id: nueva.id } });
              }}
            />
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
