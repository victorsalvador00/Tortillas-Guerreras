import { useNavigate, useParams } from '@tanstack/react-router';
import {
  esErrorApi,
  mensajeError,
  useActualizarListaPrecio,
  useCrearListaPrecio,
  useGuardarPartidasListaPrecio,
  useListarCanales,
  useListarListasPrecio,
  useObtenerListaPrecio,
  type ListaPrecioDto,
} from '@tg/api-client';
import { fechaOperativa, formatoFecha, formatoMoneda } from '@tg/shared';
import { Aviso, BarraAccion, Boton, BotonIcono, Campo, Casilla, Dialogo, EtiquetaEstado, Selector, TablaDatos, Tarjeta, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { SelectorProducto, exportarCsv } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { unidadSugerida, unidadesVenta } from './partidas';

interface DatosLista {
  nombre: string;
  canalId: string;
  vigenteDesde: string;
  activo: boolean;
}

function FormularioEncabezado({
  inicial,
  alGuardar,
  ocupado,
  error,
  textoBoton,
}: {
  inicial: DatosLista;
  alGuardar: (d: DatosLista) => void;
  ocupado: boolean;
  error: string | null;
  textoBoton: string;
}) {
  const [d, setD] = useState(inicial);
  const { data: canales } = useListarCanales();
  const sinNombre = d.nombre.trim().length === 0;
  return (
    <div className="grid gap-4 sm:grid-cols-2">
      {error && <Aviso tipo="error" className="sm:col-span-2">{error}</Aviso>}
      <Campo etiqueta="Nombre" value={d.nombre} onChange={(e) => setD({ ...d, nombre: e.target.value })} error={sinNombre ? 'Captura el nombre.' : undefined} />
      <Selector
        etiqueta="Canal"
        vacio="Sin canal"
        opciones={(canales ?? []).map((c) => ({ valor: c.id, etiqueta: c.nombre }))}
        value={d.canalId}
        onChange={(e) => setD({ ...d, canalId: e.target.value })}
      />
      <Campo etiqueta="Vigente desde" type="date" value={d.vigenteDesde} onChange={(e) => setD({ ...d, vigenteDesde: e.target.value })} />
      <div className="flex items-center justify-between gap-3">
        <Casilla etiqueta="Activa" checked={d.activo} onChange={(e) => setD({ ...d, activo: e.target.checked })} />
        <Boton cargando={ocupado} disabled={sinNombre || !d.vigenteDesde} onClick={() => alGuardar(d)}>
          {textoBoton}
        </Boton>
      </div>
    </div>
  );
}

export function ListasPrecio() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const { data, isLoading } = useListarListasPrecio();
  const crear = useCrearListaPrecio();
  const [nueva, setNueva] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const columnas = useMemo<ColumnDef<ListaPrecioDto, unknown>[]>(
    () => [
      { header: 'Nombre', accessorKey: 'nombre', cell: ({ row }) => <span className="font-medium">{row.original.nombre}</span> },
      { header: 'Vigente desde', accessorKey: 'vigenteDesde', cell: ({ row }) => formatoFecha(row.original.vigenteDesde) },
      { header: 'Precios', accessorKey: 'totalPartidas', meta: { alinear: 'fin' } },
      { header: 'Estado', accessorKey: 'activo', cell: ({ row }) => <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} /> },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Listas de precio"
        subtitulo="Una lista por canal; precios sin impuestos [SUP-07]."
        acciones={tienePermiso('ventas.configurar') && <Boton icono="add" onClick={() => setNueva(true)}>Nueva lista</Boton>}
      />
      <TablaDatos
        columnas={columnas}
        datos={data}
        cargando={isLoading}
        idFila={(l) => l.id}
        alClicFila={(l) => navegar({ to: '/ventas/listas-precio/$id', params: { id: l.id } })}
        mensajeVacio="No hay listas de precio."
      />
      <Dialogo abierto={nueva} onCambiar={setNueva} titulo="Nueva lista de precio">
        <FormularioEncabezado
          inicial={{ nombre: '', canalId: '', vigenteDesde: fechaOperativa(), activo: true }}
          ocupado={crear.isPending}
          error={error}
          textoBoton="Crear lista"
          alGuardar={async (d) => {
            setError(null);
            try {
              const lista = await crear.mutateAsync({ data: { ...d, canalId: d.canalId || null } });
              toast.success('Lista creada.');
              setNueva(false);
              await navegar({ to: '/ventas/listas-precio/$id', params: { id: lista.id } });
            } catch (e) {
              setError(mensajeError(e));
            }
          }}
        />
      </Dialogo>
    </Pagina>
  );
}

interface FilaPrecio {
  clave: string;
  productoId: string;
  productoEtiqueta: string;
  unidadId: string;
  opcionesUnidad: { valor: string; etiqueta: string }[];
  precio: string;
}

let consecutivo = 0;
const nuevaClave = () => `p${++consecutivo}`;

export function ListaPrecioDetalle() {
  const { id } = useParams({ strict: false }) as { id: string };
  const consulta = useObtenerListaPrecio(id);
  return (
    <Pagina>
      <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
        {consulta.data && <DetalleLista key={consulta.data.id} lista={consulta.data} />}
      </EstadoConsulta>
    </Pagina>
  );
}

function DetalleLista({ lista }: { lista: ListaPrecioDto }) {
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('ventas.configurar');
  const actualizar = useActualizarListaPrecio();
  const guardarPartidas = useGuardarPartidasListaPrecio();
  const [errorEncabezado, setErrorEncabezado] = useState<string | null>(null);
  const [errorPartidas, setErrorPartidas] = useState<string | null>(null);
  const [buscar, setBuscar] = useState('');
  const [filas, setFilas] = useState<FilaPrecio[]>(() =>
    lista.partidas.map((p) => ({
      clave: nuevaClave(),
      productoId: p.productoId,
      productoEtiqueta: `${p.sku} · ${p.producto}`,
      unidadId: p.unidadId,
      opcionesUnidad: [{ valor: p.unidadId, etiqueta: p.unidad }],
      precio: String(p.precioUnitario),
    })),
  );
  const cambiar = (clave: string, cambio: Partial<FilaPrecio>) => setFilas((fs) => fs.map((f) => (f.clave === clave ? { ...f, ...cambio } : f)));
  const visibles = buscar ? filas.filter((f) => f.productoEtiqueta.toLowerCase().includes(buscar.toLowerCase()) || !f.productoId) : filas;
  const duplicadas = new Set(
    filas.filter((f, i) => filas.findIndex((g) => g.productoId === f.productoId && g.unidadId === f.unidadId) !== i).map((f) => f.clave),
  );
  const invalidas = filas.some((f) => !f.productoId || !f.unidadId || !(Number(f.precio) >= 0) || f.precio === '') || duplicadas.size > 0;

  return (
    <>
      <EncabezadoPagina
        titulo={lista.nombre}
        subtitulo={`Vigente desde ${formatoFecha(lista.vigenteDesde)} · ${lista.partidas.length} precios`}
        migas={[{ etiqueta: 'Ventas' }, { etiqueta: 'Listas de precio', a: '/ventas/listas-precio' }, { etiqueta: lista.nombre }]}
        acciones={
          <Boton
            variante="secundario"
            icono="download"
            onClick={() =>
              exportarCsv(`lista-${lista.nombre}`, lista.partidas, [
                { titulo: 'SKU', valor: (p) => p.sku },
                { titulo: 'Producto', valor: (p) => p.producto },
                { titulo: 'Unidad', valor: (p) => p.unidad },
                { titulo: 'Precio sin impuestos', valor: (p) => p.precioUnitario },
              ])
            }
          >
            Exportar CSV
          </Boton>
        }
      />
      <div className="flex flex-col gap-4">
        <Tarjeta titulo="Datos de la lista">
          <fieldset disabled={!editable} className="m-0 border-0 p-0">
            <FormularioEncabezado
              inicial={{ nombre: lista.nombre, canalId: lista.canalId ?? '', vigenteDesde: lista.vigenteDesde, activo: lista.activo }}
              ocupado={actualizar.isPending}
              error={errorEncabezado}
              textoBoton="Guardar datos"
              alGuardar={async (d) => {
                setErrorEncabezado(null);
                try {
                  await actualizar.mutateAsync({ id: lista.id, data: { ...d, canalId: d.canalId || null } });
                  toast.success('Lista actualizada.');
                } catch (e) {
                  setErrorEncabezado(mensajeError(e));
                }
              }}
            />
          </fieldset>
        </Tarjeta>

        <Tarjeta
          titulo="Precios"
          subtitulo="Precio unitario sin impuestos por producto y unidad de venta. Bajan a la app de ruta al sincronizar."
          acciones={
            editable && (
              <Boton
                variante="secundario"
                tamano="chico"
                icono="add"
                onClick={() => setFilas((fs) => [...fs, { clave: nuevaClave(), productoId: '', productoEtiqueta: '', unidadId: '', opcionesUnidad: [], precio: '' }])}
              >
                Agregar precio
              </Boton>
            )
          }
        >
          {errorPartidas && <Aviso tipo="error" className="mb-3">{errorPartidas}</Aviso>}
          <Campo etiqueta="Filtrar productos" type="search" className="mb-3 max-w-sm" value={buscar} onChange={(e) => setBuscar(e.target.value)} />
          <div className="flex flex-col gap-2">
            {visibles.length === 0 && <p className="m-0 text-sm text-muted">Sin precios.</p>}
            {visibles.map((f) => (
              <div key={f.clave} className="grid items-start gap-2 rounded-xl border border-row-divider p-3 sm:grid-cols-[minmax(0,1fr)_230px_216px_auto]">
                <SelectorProducto
                  compacto
                  valor={f.productoId || null}
                  producto={f.productoId ? { id: f.productoId, sku: f.productoEtiqueta.split(' · ')[0], nombre: f.productoEtiqueta.split(' · ').slice(1).join(' · ') } : null}
                  filtro={{ esVendible: true }}
                  deshabilitado={!editable}
                  onCambiar={(p) =>
                    cambiar(f.clave, {
                      productoId: p?.id ?? '',
                      productoEtiqueta: p ? `${p.sku} · ${p.nombre}` : '',
                      opcionesUnidad: unidadesVenta(p),
                      unidadId: p ? unidadSugerida(p) : '',
                    })
                  }
                />
                <Selector
                  compacto
                  etiqueta="Unidad"
                  opciones={f.opcionesUnidad}
                  vacio={f.opcionesUnidad.length ? undefined : 'Unidad'}
                  value={f.unidadId}
                  disabled={!editable || !f.productoId}
                  onChange={(e) => cambiar(f.clave, { unidadId: e.target.value })}
                />
                <Campo
                  compacto
                  etiqueta="Precio sin impuestos"
                  type="number"
                  step="any"
                  min="0"
                  inputMode="decimal"
                  prefijo="$"
                  value={f.precio}
                  disabled={!editable}
                  onChange={(e) => cambiar(f.clave, { precio: e.target.value })}
                />
                {editable ? (
                  <BotonIcono icono="delete" etiqueta="Quitar precio" onClick={() => setFilas((fs) => fs.filter((x) => x.clave !== f.clave))} />
                ) : (
                  <span className="pt-3 text-right text-sm tabular-nums">{formatoMoneda(Number(f.precio))}</span>
                )}
                {duplicadas.has(f.clave) && <p className="m-0 text-xs text-error sm:col-span-4">Producto y unidad repetidos.</p>}
              </div>
            ))}
          </div>
        </Tarjeta>
        {editable && (
          <BarraAccion resumen={invalidas ? 'Completa producto, unidad y precio en cada renglón.' : `${filas.length} precios`}>
            <Boton
              disabled={invalidas}
              cargando={guardarPartidas.isPending}
              onClick={async () => {
                setErrorPartidas(null);
                try {
                  await guardarPartidas.mutateAsync({
                    id: lista.id,
                    data: { partidas: filas.map((f) => ({ productoId: f.productoId, unidadId: f.unidadId, precioUnitario: Number(f.precio) })) },
                  });
                  toast.success('Precios guardados.');
                } catch (e) {
                  setErrorPartidas(esErrorApi(e) ? e.message : mensajeError(e));
                }
              }}
            >
              Guardar precios
            </Boton>
          </BarraAccion>
        )}
      </div>
    </>
  );
}
