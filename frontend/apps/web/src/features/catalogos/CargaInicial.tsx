import {
  clienteApi,
  importarClientes,
  importarExistencias,
  importarGruposClientes,
  importarPrecios,
  importarProductos,
  importarProveedores,
  importarRecetas,
  importarRutas,
  importarRutasClientes,
  mensajeError,
  useColumnasImportacion,
  type ErrorImportacionDto,
  type ResultadoImportacionDto,
} from '@tg/api-client';
import { Aviso, Boton, DialogoConfirmacion, Icono, TablaDatos, Tarjeta, toast, type ColumnDef } from '@tg/ui';
import { useRef, useState } from 'react';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';

type Entidad =
  | 'productos'
  | 'clientes'
  | 'proveedores'
  | 'recetas'
  | 'existencias'
  | 'grupos_clientes'
  | 'rutas'
  | 'rutas_clientes'
  | 'precios';

type Importador = (body: { archivo?: Blob }, params?: { validarSolo?: boolean }) => Promise<ResultadoImportacionDto>;

/** En el orden sugerido de carga: cada plantilla usa claves de las anteriores (grupo → cliente → ruta → precios). */
export const ENTIDADES: { entidad: Entidad; titulo: string; descripcion: string; icono: string; permiso: string; importar: Importador }[] = [
  {
    entidad: 'productos',
    titulo: 'Productos',
    descripcion: 'Alta o actualización por SKU, con unidades de compra y venta.',
    icono: 'inventory_2',
    permiso: 'catalogos.editar',
    importar: importarProductos,
  },
  {
    entidad: 'proveedores',
    titulo: 'Proveedores',
    descripcion: 'Alta o actualización por código.',
    icono: 'storefront',
    permiso: 'compras.editar',
    importar: importarProveedores,
  },
  {
    entidad: 'grupos_clientes',
    titulo: 'Grupos de clientes',
    descripcion: 'Cadenas o grupos (Sanborns…) con su canal y la lista de precios que heredan sus clientes.',
    icono: 'workspaces',
    permiso: 'ventas.configurar',
    importar: importarGruposClientes,
  },
  {
    entidad: 'clientes',
    titulo: 'Clientes',
    descripcion: 'Con canal (AUTO, DET, INST, CEDIS), grupo, lista de precios y datos fiscales.',
    icono: 'groups',
    permiso: 'ventas.clientes',
    importar: importarClientes,
  },
  {
    entidad: 'rutas',
    titulo: 'Rutas',
    descripcion: 'Alta o actualización por código, con canal, chofer y vehículo; cada ruta nueva lleva su almacén y cliente genérico.',
    icono: 'route',
    permiso: 'logistica.configurar',
    importar: importarRutas,
  },
  {
    entidad: 'rutas_clientes',
    titulo: 'Clientes por ruta',
    descripcion: 'Orden y días de visita; cada ruta del archivo queda solo con los clientes que trae.',
    icono: 'pin_drop',
    permiso: 'logistica.configurar',
    importar: importarRutasClientes,
  },
  {
    entidad: 'precios',
    titulo: 'Precios',
    descripcion: 'Por lista o especiales por cliente; agrega o actualiza sin borrar los que no vienen.',
    icono: 'sell',
    permiso: 'ventas.configurar',
    importar: importarPrecios,
  },
  {
    entidad: 'recetas',
    titulo: 'Recetas',
    descripcion: 'Una fila por componente; se crean como nueva versión y se pueden publicar.',
    icono: 'menu_book',
    permiso: 'produccion.recetas',
    importar: importarRecetas,
  },
  {
    entidad: 'existencias',
    titulo: 'Existencias iniciales',
    descripcion: 'Entran al kardex como ajuste de entrada con costo ("Carga inicial").',
    icono: 'warehouse',
    permiso: 'inventario.ajustar',
    importar: importarExistencias,
  },
];

const COLUMNAS_ERRORES: ColumnDef<ErrorImportacionDto, unknown>[] = [
  { header: 'Fila', accessorKey: 'fila', cell: ({ row }) => (row.original.fila > 0 ? row.original.fila : '—'), meta: { alinear: 'fin' } },
  { header: 'Columna', accessorKey: 'campo', cell: ({ row }) => row.original.campo ?? 'General' },
  { header: 'Problema', accessorKey: 'mensaje' },
];

/** Resumen legible del resultado de una importación o vista previa. */
export function resumenImportacion(r: ResultadoImportacionDto): string {
  if (r.errores.length > 0) {
    return `${r.errores.length} ${r.errores.length === 1 ? 'problema' : 'problemas'} en ${r.filas} filas; no se importó nada.`;
  }
  const verbo = r.validarSolo ? 'Se crearían' : 'Se crearon';
  const verboAct = r.validarSolo ? 'se actualizarían' : 'se actualizaron';
  return `${r.filas} filas: ${verbo} ${r.creados} y ${verboAct} ${r.actualizados}.`;
}

async function descargarPlantilla(entidad: Entidad) {
  const blob = await clienteApi<Blob>({ url: `/api/v1/importacion/${entidad}/plantilla`, method: 'GET', responseType: 'blob' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = `plantilla-${entidad}.csv`;
  a.click();
  URL.revokeObjectURL(a.href);
}

function TarjetaImportacion({ definicion }: { definicion: (typeof ENTIDADES)[number] }) {
  const { tienePermiso } = useSesion();
  const permitido = tienePermiso(definicion.permiso);
  const entrada = useRef<HTMLInputElement>(null);
  const [archivo, setArchivo] = useState<File | null>(null);
  const [resultado, setResultado] = useState<ResultadoImportacionDto | null>(null);
  const [validando, setValidando] = useState(false);
  const [confirmar, setConfirmar] = useState(false);
  const [verColumnas, setVerColumnas] = useState(false);
  const columnas = useColumnasImportacion(definicion.entidad, { query: { enabled: verColumnas } });

  const validar = async (f: File) => {
    setValidando(true);
    setResultado(null);
    try {
      setResultado(await definicion.importar({ archivo: f }, { validarSolo: true }));
    } catch (e) {
      toast.error(mensajeError(e));
    } finally {
      setValidando(false);
    }
  };

  return (
    <Tarjeta
      titulo={
        <span className="inline-flex items-center gap-2">
          <Icono nombre={definicion.icono} /> {definicion.titulo}
        </span>
      }
      subtitulo={definicion.descripcion}
      acciones={
        <Boton variante="secundario" tamano="chico" icono="download" onClick={() => descargarPlantilla(definicion.entidad).catch((e) => toast.error(mensajeError(e)))}>
          Plantilla
        </Boton>
      }
    >
      {!permitido ? (
        <p className="m-0 text-sm text-muted">No tienes permiso para importar {definicion.titulo.toLowerCase()}.</p>
      ) : (
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <input
              ref={entrada}
              type="file"
              accept=".csv,text/csv"
              className="sr-only"
              aria-label={`Archivo CSV de ${definicion.titulo.toLowerCase()}`}
              onChange={(e) => {
                const f = e.target.files?.[0] ?? null;
                setArchivo(f);
                if (f) void validar(f);
                e.target.value = '';
              }}
            />
            <Boton variante="contorno" icono="upload_file" cargando={validando} onClick={() => entrada.current?.click()}>
              {archivo ? 'Elegir otro archivo' : 'Elegir archivo CSV'}
            </Boton>
            {archivo && <span className="text-sm text-muted">{archivo.name}</span>}
            <button type="button" className="ml-auto text-sm text-primary hover:underline" onClick={() => setVerColumnas((v) => !v)}>
              {verColumnas ? 'Ocultar columnas' : 'Ver columnas'}
            </button>
          </div>

          {verColumnas && (
            <ul className="m-0 grid list-none gap-1 rounded-xl bg-soft p-3 text-sm sm:grid-cols-2">
              {(columnas.data ?? []).map((c) => (
                <li key={c.nombre}>
                  <code>{c.nombre}</code>
                  {c.obligatoria && <span className="text-error"> *</span>}
                  {c.descripcion && <span className="text-muted"> · {c.descripcion}</span>}
                </li>
              ))}
            </ul>
          )}

          {resultado && (
            <>
              <Aviso tipo={resultado.errores.length ? 'error' : resultado.aplicado ? 'exito' : 'info'} titulo={resultado.validarSolo ? 'Vista previa' : 'Importación'}>
                {resumenImportacion(resultado)}
              </Aviso>
              {resultado.errores.length > 0 && (
                <TablaDatos columnas={COLUMNAS_ERRORES} datos={resultado.errores} ordenable={false} />
              )}
              {resultado.validarSolo && resultado.errores.length === 0 && archivo && (
                <div>
                  <Boton icono="publish" onClick={() => setConfirmar(true)}>
                    Importar {resultado.filas} filas
                  </Boton>
                </div>
              )}
            </>
          )}
        </div>
      )}

      {archivo && (
        <DialogoConfirmacion
          abierto={confirmar}
          onCambiar={setConfirmar}
          titulo={`Importar ${definicion.titulo.toLowerCase()}`}
          mensaje={`Se importará ${archivo.name} en una sola operación: si alguna fila falla no se guarda nada.`}
          textoConfirmar="Importar"
          alConfirmar={async () => {
            const r = await definicion.importar({ archivo }, { validarSolo: false });
            setResultado(r);
            if (r.aplicado) {
              toast.success(resumenImportacion(r));
              setArchivo(null);
            } else {
              throw new Error(resumenImportacion(r));
            }
          }}
        />
      )}
    </Tarjeta>
  );
}

/** Carga inicial (T-7.3): plantillas CSV, vista previa con errores por fila e importación en una transacción. */
export function CargaInicial() {
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Carga inicial"
        subtitulo="Descarga la plantilla, llénala en Excel y guárdala como CSV (UTF-8). Primero verás una vista previa; nada se guarda hasta que confirmes."
      />
      <div className="grid gap-4 lg:grid-cols-2">
        {ENTIDADES.map((d) => (
          <TarjetaImportacion key={d.entidad} definicion={d} />
        ))}
      </div>
    </Pagina>
  );
}
