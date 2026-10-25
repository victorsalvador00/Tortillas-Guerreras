import {
  listarProductos,
  useListarAlmacenes,
  useListarMotivosMerma,
  type AlmacenDto,
  type ListarProductosParams,
  type MovimientoDto,
  type ProductoDto,
} from '@tg/api-client';
import { formatoCantidad, formatoFechaHora, formatoMoneda } from '@tg/shared';
import { BuscadorSeleccion, Selector, TablaDatos, cn, etiquetaEnum, type ColumnDef, type OpcionBusqueda } from '@tg/ui';
import { useCallback, useMemo, type ReactNode } from 'react';

/** Selector de producto con búsqueda en el API (SKU, nombre o código de barras). */
export function SelectorProducto({
  etiqueta = 'Producto',
  valor,
  producto,
  onCambiar,
  filtro,
  error,
  compacto,
  className,
  deshabilitado,
  permitidos,
}: {
  etiqueta?: string;
  valor: string | null | undefined;
  producto?: Pick<ProductoDto, 'id' | 'sku' | 'nombre'> | null;
  onCambiar: (producto: ProductoDto | null) => void;
  filtro?: Omit<ListarProductosParams, 'buscar' | 'pagina' | 'tamano'>;
  error?: string;
  compacto?: boolean;
  className?: string;
  deshabilitado?: boolean;
  /** Si se indica, solo se ofrecen estos productos (p. ej. el catálogo de un proveedor). */
  permitidos?: ReadonlySet<string>;
}) {
  const cache = useMemo(() => new Map<string, ProductoDto>(), []);
  const filtroClave = JSON.stringify(filtro ?? {});
  const permitidosClave = permitidos ? [...permitidos].sort().join(',') : null;
  const buscar = useCallback(
    async (texto: string): Promise<OpcionBusqueda[]> => {
      const r = await listarProductos({ ...(JSON.parse(filtroClave) as object), activo: true, buscar: texto || undefined, tamano: 30 });
      r.items.forEach((p) => cache.set(p.id, p));
      const lista = permitidosClave == null ? r.items : r.items.filter((p) => permitidosClave.split(',').includes(p.id));
      return lista.map((p) => ({ valor: p.id, etiqueta: `${p.sku} · ${p.nombre}`, detalle: `${etiquetaEnum(p.tipo)} · ${p.unidadBaseClave}` }));
    },
    [cache, filtroClave, permitidosClave],
  );
  return (
    <BuscadorSeleccion
      etiqueta={etiqueta}
      valor={valor}
      seleccion={producto ? { valor: producto.id, etiqueta: `${producto.sku} · ${producto.nombre}` } : null}
      buscar={buscar}
      onCambiar={(id) => onCambiar(id ? (cache.get(id) ?? null) : null)}
      error={error}
      compacto={compacto}
      className={className}
      deshabilitado={deshabilitado}
    />
  );
}

/** Lista de almacenes (sin los de ruta, salvo que se pidan). */
export function SelectorAlmacen({
  etiqueta = 'Almacén',
  incluirRutas = false,
  soloRutas = false,
  vacio,
  className,
  error,
  value,
  onChange,
  disabled,
}: {
  etiqueta?: string;
  incluirRutas?: boolean;
  soloRutas?: boolean;
  vacio?: string;
  className?: string;
  error?: string;
  value: string;
  onChange: (valor: string) => void;
  disabled?: boolean;
}) {
  const { data } = useListarAlmacenes({ activo: true });
  const opciones = (data ?? [])
    .filter((a: AlmacenDto) => (soloRutas ? a.tipo === 'Ruta' : incluirRutas || a.tipo !== 'Ruta'))
    .map((a) => ({ valor: a.id, etiqueta: `${a.codigo} · ${a.nombre}` }));
  return (
    <Selector
      etiqueta={etiqueta}
      opciones={opciones}
      vacio={vacio}
      className={className}
      error={error}
      value={value}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value)}
    />
  );
}

/** Motivos de merma filtrados por dónde aplican. */
export function SelectorMotivo({
  aplica,
  etiqueta = 'Motivo',
  value,
  onChange,
  error,
  className,
  vacio = 'Elige un motivo',
}: {
  aplica: 'produccion' | 'almacen' | 'ruta' | 'retorno';
  etiqueta?: string;
  value: string;
  onChange: (valor: string) => void;
  error?: string;
  className?: string;
  vacio?: string;
}) {
  const { data } = useListarMotivosMerma({ aplica, activo: true });
  return (
    <Selector
      etiqueta={etiqueta}
      vacio={vacio}
      opciones={(data ?? []).map((m) => ({ valor: m.id, etiqueta: m.nombre }))}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      error={error}
      className={className}
    />
  );
}

const ETIQUETAS_MOVIMIENTO: Record<string, string> = {
  EntradaCompra: 'Entrada por compra',
  DevolucionProveedor: 'Devolución a proveedor',
  EntradaProduccion: 'Entrada de producción',
  SalidaConsumo: 'Consumo',
  Merma: 'Merma',
  TraspasoSalida: 'Traspaso (salida)',
  TraspasoEntrada: 'Traspaso (entrada)',
  Venta: 'Venta',
  RetornoCliente: 'Retorno de cliente',
  AjusteEntrada: 'Ajuste (entrada)',
  AjusteSalida: 'Ajuste (salida)',
};

export function etiquetaMovimiento(tipo: string): string {
  return ETIQUETAS_MOVIMIENTO[tipo] ?? etiquetaEnum(tipo);
}

/** Cantidad con signo y color: verde entradas, rojo salidas. */
export function CantidadConSigno({ valor, unidad }: { valor: number; unidad?: string }) {
  return (
    <span className={cn('font-medium tabular-nums', valor < 0 ? 'text-[var(--tg-status-red-fg)]' : 'text-[var(--tg-status-green-fg)]')}>
      {valor > 0 ? '+' : ''}
      {formatoCantidad(valor)}
      {unidad ? ` ${unidad}` : ''}
    </span>
  );
}

/** Movimientos de inventario generados por un documento (07-web: mostrar el resultado de cada acción). */
export function TablaMovimientos({ movimientos, cargando, conCosto = true }: { movimientos: MovimientoDto[] | undefined; cargando?: boolean; conCosto?: boolean }) {
  const columnas = useMemo<ColumnDef<MovimientoDto, unknown>[]>(
    () => [
      { header: 'Fecha', accessorKey: 'fechaHora', cell: ({ row }) => formatoFechaHora(row.original.fechaHora), meta: { ocultarEnMovil: true } },
      { header: 'Movimiento', accessorKey: 'tipo', cell: ({ row }) => etiquetaMovimiento(row.original.tipo) },
      { header: 'Almacén', accessorKey: 'almacenCodigo' },
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.producto}</span>
            <span className="text-xs text-muted">
              {row.original.sku}
              {row.original.lote ? ` · Lote ${row.original.lote}` : ''}
            </span>
          </span>
        ),
      },
      { header: 'Cantidad', accessorKey: 'cantidad', cell: ({ row }) => <CantidadConSigno valor={row.original.cantidad} />, meta: { alinear: 'fin' } },
      ...(conCosto
        ? ([
            { header: 'Costo unitario', accessorKey: 'costoUnitario', cell: ({ row }) => formatoMoneda(row.original.costoUnitario), meta: { alinear: 'fin', ocultarEnMovil: true } },
            { header: 'Costo total', accessorKey: 'costoTotal', cell: ({ row }) => formatoMoneda(row.original.costoTotal), meta: { alinear: 'fin' } },
          ] as ColumnDef<MovimientoDto, unknown>[])
        : []),
      { header: 'Existencia', accessorKey: 'existenciaResultante', cell: ({ row }) => formatoCantidad(row.original.existenciaResultante), meta: { alinear: 'fin', ocultarEnMovil: true } },
    ],
    [conCosto],
  );
  return <TablaDatos columnas={columnas} datos={movimientos} cargando={cargando} mensajeVacio="Sin movimientos de inventario." ordenable={false} />;
}

/** Barra de filtros de los listados (fila que se acomoda en celular). */
export function BarraFiltros({ children, acciones }: { children: ReactNode; acciones?: ReactNode }) {
  return (
    <div className="mb-4 flex flex-wrap items-start gap-3">
      <div className="grid flex-1 grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">{children}</div>
      {acciones && <div className="flex flex-wrap items-center gap-2 pt-2">{acciones}</div>}
    </div>
  );
}

/** Descarga un CSV (UTF-8 con BOM para Excel) con las columnas indicadas. */
export function exportarCsv<T>(nombre: string, filas: T[], columnas: { titulo: string; valor: (fila: T) => unknown }[]): void {
  const escapar = (v: unknown) => {
    const s = v == null ? '' : String(v);
    return /[",\n;]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  const lineas = [columnas.map((c) => escapar(c.titulo)).join(','), ...filas.map((f) => columnas.map((c) => escapar(c.valor(f))).join(','))];
  const blob = new Blob(['﻿' + lineas.join('\r\n')], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = nombre.endsWith('.csv') ? nombre : `${nombre}.csv`;
  a.click();
  URL.revokeObjectURL(url);
}

/** Opciones `{valor, etiqueta}` desde un enum del API. */
export function opcionesEnum(valores: readonly string[], etiquetas?: Record<string, string>) {
  return valores.map((v) => ({ valor: v, etiqueta: etiquetas?.[v] ?? etiquetaEnum(v) }));
}
