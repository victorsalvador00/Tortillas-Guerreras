import {
  esErrorApi,
  mensajeError,
  useActualizarProductoProveedor,
  useAgregarProductoProveedor,
  useListarProductosProveedor,
  useObtenerProducto,
  type ProductoDto,
  type ProductoProveedorDto,
  type ProveedorDto,
} from '@tg/api-client';
import { formatoFechaHora, formatoMoneda } from '@tg/shared';
import { Aviso, Boton, BotonIcono, Campo, Casilla, Dialogo, EtiquetaEstado, Interruptor, Selector, TablaDatos, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { SelectorProducto } from '@/componentes/comunes';
import { unidadesCompra } from './calculos';

/** Precio pactado opcional: vacío = sin precio; si viene, número ≥ 0. */
export function validarPrecioPactado(texto: string): string | null {
  if (texto.trim() === '') return null;
  const n = Number(texto);
  return Number.isFinite(n) && n >= 0 ? null : 'El precio no puede ser negativo.';
}

/** Catálogo de productos del proveedor (RN-COM-10 a RN-COM-12): qué se le compra, a qué precio pactado y en cuántos días. */
export function DialogoProductosProveedor({ proveedor, editable, onCerrar }: { proveedor: ProveedorDto; editable: boolean; onCerrar: () => void }) {
  const [inactivos, setInactivos] = useState(false);
  const { data, isLoading } = useListarProductosProveedor(proveedor.id, { incluirInactivos: inactivos });
  const [formulario, setFormulario] = useState<{ abierto: boolean; registro: ProductoProveedorDto | null }>({ abierto: false, registro: null });
  const puedeEditar = editable && proveedor.activo;

  const columnas = useMemo<ColumnDef<ProductoProveedorDto, unknown>[]>(
    () => [
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.producto}</span>
            <span className="text-xs text-muted">
              {row.original.sku}
              {row.original.claveProveedor ? ` · clave proveedor ${row.original.claveProveedor}` : ''}
            </span>
          </span>
        ),
      },
      {
        header: 'Precio pactado',
        accessorKey: 'precio',
        meta: { alinear: 'fin' },
        cell: ({ row }) =>
          row.original.precio != null ? (
            <span className="tabular-nums">
              {formatoMoneda(row.original.precio)} / {row.original.unidad}
              {row.original.factorABase !== 1 && row.original.precioBase != null && (
                <span className="block text-xs text-muted">
                  {formatoMoneda(row.original.precioBase)} / {row.original.unidadBase}
                </span>
              )}
            </span>
          ) : (
            <span className="text-muted">Sin precio · {row.original.unidad}</span>
          ),
      },
      {
        header: 'Último pagado',
        accessorKey: 'ultimoPrecio',
        meta: { alinear: 'fin', ocultarEnMovil: true },
        cell: ({ row }) =>
          row.original.ultimaCompraEn ? (
            <span className="tabular-nums">
              {formatoMoneda(row.original.ultimoPrecio ?? 0)} / {row.original.ultimaUnidad}
              <span className="block text-xs text-muted">{formatoFechaHora(row.original.ultimaCompraEn)}</span>
            </span>
          ) : (
            '—'
          ),
      },
      {
        header: 'Entrega',
        accessorKey: 'diasEntrega',
        meta: { alinear: 'fin', ocultarEnMovil: true },
        cell: ({ row }) => `${row.original.diasEntrega} ${row.original.diasEntrega === 1 ? 'día' : 'días'}`,
      },
      {
        header: 'Preferido',
        accessorKey: 'preferido',
        cell: ({ row }) => (row.original.preferido ? <EtiquetaEstado estado="Preferido" /> : ''),
      },
      { header: 'Estado', accessorKey: 'activo', cell: ({ row }) => <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} /> },
      ...(puedeEditar
        ? [
            {
              id: 'acciones',
              header: '',
              enableSorting: false,
              meta: { alinear: 'fin' as const },
              cell: ({ row }: { row: { original: ProductoProveedorDto } }) => (
                <BotonIcono
                  icono="edit"
                  etiqueta={`Editar ${row.original.sku}`}
                  plano
                  onClick={() => setFormulario({ abierto: true, registro: row.original })}
                />
              ),
            },
          ]
        : []),
    ],
    [puedeEditar],
  );

  return (
    <Dialogo
      abierto
      onCambiar={(a) => !a && onCerrar()}
      titulo={`Productos de ${proveedor.razonSocial}`}
      ancho="grande"
      descripcion="Lo que se le puede comprar. Las órdenes de compra solo aceptan productos activos de este catálogo; precios sin IVA."
    >
      <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
        <Interruptor etiqueta="Mostrar inactivos" checked={inactivos} onChange={(e) => setInactivos(e.target.checked)} />
        {puedeEditar && (
          <Boton icono="add" onClick={() => setFormulario({ abierto: true, registro: null })}>
            Agregar producto
          </Boton>
        )}
      </div>
      {editable && !proveedor.activo && (
        <Aviso tipo="advertencia" className="mb-3">
          El proveedor está inactivo: reactívalo para editar sus productos.
        </Aviso>
      )}
      <TablaDatos
        columnas={columnas}
        datos={data}
        cargando={isLoading}
        idFila={(p) => p.productoId}
        mensajeVacio="Todavía no tiene productos. Agrégalos para poder hacerle órdenes de compra."
      />
      {formulario.abierto && (
        <FormularioProductoProveedor
          proveedorId={proveedor.id}
          registro={formulario.registro}
          onCerrar={() => setFormulario({ abierto: false, registro: null })}
        />
      )}
    </Dialogo>
  );
}

function FormularioProductoProveedor({
  proveedorId,
  registro,
  onCerrar,
}: {
  proveedorId: string;
  registro: ProductoProveedorDto | null;
  onCerrar: () => void;
}) {
  const agregar = useAgregarProductoProveedor();
  const actualizar = useActualizarProductoProveedor();
  const existente = useObtenerProducto(registro?.productoId ?? '', { query: { enabled: !!registro } });
  const [elegido, setElegido] = useState<ProductoDto | null>(null);
  const producto = registro ? (existente.data ?? null) : elegido;
  const [unidadId, setUnidadId] = useState(registro?.unidadId ?? '');
  const [clave, setClave] = useState(registro?.claveProveedor ?? '');
  const [precio, setPrecio] = useState(registro?.precio != null ? String(registro.precio) : '');
  const [dias, setDias] = useState(String(registro?.diasEntrega ?? 0));
  const [preferido, setPreferido] = useState(registro?.preferido ?? false);
  const [activo, setActivo] = useState(registro?.activo ?? true);
  const [errores, setErrores] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const elegir = (p: ProductoDto | null) => {
    setElegido(p);
    // Sugiere la unidad de compra; si no tiene, la base.
    setUnidadId(p ? (p.conversiones.find((c) => c.usoCompra)?.unidadId ?? p.unidadBaseId) : '');
  };

  const guardar = async () => {
    const e: Record<string, string> = {};
    if (!producto) e.productoId = 'Elige el producto.';
    if (!unidadId) e.unidadId = 'Elige la unidad de compra.';
    const errorPrecio = validarPrecioPactado(precio);
    if (errorPrecio) e.precio = errorPrecio;
    if (!/^\d+$/.test(dias.trim()) || Number(dias) > 365) e.diasEntrega = 'De 0 a 365.';
    setErrores(e);
    if (Object.keys(e).length > 0) return;
    setError(null);
    setGuardando(true);
    const datos = {
      unidadId,
      claveProveedor: clave.trim() || null,
      precio: precio.trim() === '' ? null : Number(precio),
      diasEntrega: Number(dias),
      preferido: preferido && activo,
    };
    try {
      if (registro) await actualizar.mutateAsync({ id: proveedorId, productoId: registro.productoId, data: { ...datos, activo } });
      else await agregar.mutateAsync({ id: proveedorId, data: { ...datos, productoId: producto!.id } });
      toast.success('Producto del proveedor guardado.');
      onCerrar();
    } catch (err) {
      if (esErrorApi(err)) setErrores(Object.fromEntries(Object.entries(err.errores).map(([c, m]) => [c, m[0]])));
      setError(mensajeError(err));
    } finally {
      setGuardando(false);
    }
  };

  return (
    <Dialogo
      abierto
      onCambiar={(a) => !a && onCerrar()}
      titulo={registro ? `Editar ${registro.sku}` : 'Agregar producto'}
      acciones={
        <>
          <Boton variante="texto" onClick={onCerrar}>
            Cancelar
          </Boton>
          <Boton cargando={guardando} onClick={guardar}>
            Guardar
          </Boton>
        </>
      }
    >
      <div className="grid gap-4 sm:grid-cols-2">
        {error && (
          <Aviso tipo="error" className="sm:col-span-2">
            {error}
          </Aviso>
        )}
        <SelectorProducto
          etiqueta="Producto comprable"
          className="sm:col-span-2"
          valor={producto?.id ?? registro?.productoId}
          producto={producto ?? (registro ? { id: registro.productoId, sku: registro.sku, nombre: registro.producto } : null)}
          filtro={{ esComprable: true }}
          onCambiar={elegir}
          error={errores.productoId}
          deshabilitado={!!registro}
        />
        <Selector
          etiqueta="Unidad de compra"
          vacio="Elige"
          opciones={producto ? unidadesCompra(producto) : []}
          value={unidadId}
          onChange={(ev) => setUnidadId(ev.target.value)}
          error={errores.unidadId}
        />
        <Campo etiqueta="Clave del proveedor (opcional)" value={clave} maxLength={50} onChange={(ev) => setClave(ev.target.value)} error={errores.claveProveedor} />
        <Campo
          etiqueta="Precio pactado sin IVA (opcional)"
          type="number"
          inputMode="decimal"
          step="any"
          value={precio}
          onChange={(ev) => setPrecio(ev.target.value)}
          error={errores.precio}
        />
        <Campo
          etiqueta="Días de entrega"
          type="number"
          inputMode="numeric"
          value={dias}
          onChange={(ev) => setDias(ev.target.value)}
          error={errores.diasEntrega}
        />
        <Casilla
          etiqueta="Proveedor preferido de este producto"
          descripcion="Marcarlo quita la preferencia del proveedor que la tenía."
          className="sm:col-span-2"
          checked={preferido && activo}
          disabled={!activo}
          onChange={(ev) => setPreferido(ev.target.checked)}
        />
        {registro && (
          <Interruptor
            etiqueta="Activo en el catálogo"
            className="sm:col-span-2"
            checked={activo}
            onChange={(ev) => setActivo(ev.target.checked)}
          />
        )}
      </div>
    </Dialogo>
  );
}
