import { zodResolver } from '@hookform/resolvers/zod';
import {
  esErrorApi,
  mensajeError,
  useActualizarAlmacen,
  useConsultarKardex,
  useConsultarValuacion,
  useCrearAlmacen,
  useListarAlmacenes,
  useListarExistencias,
  type AlmacenDto,
  type DocumentoInventario,
  type ExistenciaDto,
  type MovimientoDto,
  type ProductoDto,
  type ValuacionDto,
} from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda, sumarDias } from '@tg/shared';
import {
  Aviso,
  Boton,
  BotonIcono,
  Campo,
  Casilla,
  Dialogo,
  EtiquetaEstado,
  Selector,
  TablaDatos,
  Tarjeta,
  cn,
  etiquetaEnum,
  toast,
  type ColumnDef,
} from '@tg/ui';
import { useMemo, useState } from 'react';
import { useForm, type Path } from 'react-hook-form';
import { z } from 'zod';
import {
  BarraFiltros,
  CantidadConSigno,
  SelectorAlmacen,
  SelectorProducto,
  etiquetaMovimiento,
  exportarCsv,
  opcionesEnum,
} from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { alertaExistencia, type AlertaExistencia } from './reglas';

const ETIQUETA_ALERTA: Record<Exclude<AlertaExistencia, null>, { etiqueta: string; color: 'rojo' | 'naranja' | 'amarillo' }> = {
  caducado: { etiqueta: 'Caducado', color: 'rojo' },
  'por-caducar': { etiqueta: 'Por caducar', color: 'naranja' },
  'bajo-minimo': { etiqueta: 'Bajo mínimo', color: 'amarillo' },
};

// ───────────────────────────── Existencias ─────────────────────────────

export function Existencias() {
  const [almacenId, setAlmacenId] = useState('');
  const [buscar, setBuscar] = useState('');
  const [soloConExistencia, setSoloConExistencia] = useState(true);
  const [pagina, setPagina] = useState(1);
  const { data, isLoading, isFetching } = useListarExistencias({
    almacenId: almacenId || undefined,
    buscar: buscar || undefined,
    soloConExistencia,
    pagina,
    tamano: 50,
  });

  const columnas = useMemo<ColumnDef<ExistenciaDto, unknown>[]>(
    () => [
      { header: 'Almacén', accessorKey: 'almacenCodigo' },
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
      { header: 'Lote', accessorKey: 'lote', cell: ({ row }) => row.original.lote || '—' },
      {
        header: 'Caducidad',
        accessorKey: 'fechaCaducidad',
        cell: ({ row }) => formatoFecha(row.original.fechaCaducidad ?? null),
        meta: { ocultarEnMovil: true },
      },
      {
        header: 'Existencia',
        accessorKey: 'cantidad',
        cell: ({ row }) => (
          <span className={cn('font-medium', row.original.cantidad < 0 && 'text-[var(--tg-status-red-fg)]')}>
            {formatoCantidad(row.original.cantidad)} {row.original.unidad}
          </span>
        ),
        meta: { alinear: 'fin' },
      },
      {
        header: 'Mínimo',
        accessorKey: 'stockMinimo',
        cell: ({ row }) => (row.original.stockMinimo != null ? formatoCantidad(row.original.stockMinimo) : '—'),
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      {
        header: 'Alerta',
        id: 'alerta',
        enableSorting: false,
        cell: ({ row }) => {
          const alerta = alertaExistencia(row.original);
          return alerta ? <EtiquetaEstado etiqueta={ETIQUETA_ALERTA[alerta].etiqueta} color={ETIQUETA_ALERTA[alerta].color} /> : null;
        },
      },
    ],
    [],
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Existencias"
        subtitulo="Existencia por almacén, producto y lote. El mínimo se compara por renglón."
        acciones={
          <Boton
            variante="secundario"
            icono="download"
            disabled={!data?.items.length}
            onClick={() =>
              exportarCsv('existencias', data?.items ?? [], [
                { titulo: 'Almacén', valor: (e) => e.almacenCodigo },
                { titulo: 'SKU', valor: (e) => e.sku },
                { titulo: 'Producto', valor: (e) => e.producto },
                { titulo: 'Lote', valor: (e) => e.lote },
                { titulo: 'Caducidad', valor: (e) => e.fechaCaducidad ?? '' },
                { titulo: 'Existencia', valor: (e) => e.cantidad },
                { titulo: 'Unidad', valor: (e) => e.unidad },
              ])
            }
          >
            Exportar CSV
          </Boton>
        }
      />
      <BarraFiltros>
        <SelectorAlmacen
          incluirRutas
          vacio="Todos los almacenes"
          value={almacenId}
          onChange={(v) => {
            setAlmacenId(v);
            setPagina(1);
          }}
        />
        <Campo
          etiqueta="Buscar producto (SKU o nombre)"
          type="search"
          value={buscar}
          onChange={(e) => {
            setBuscar(e.target.value);
            setPagina(1);
          }}
        />
        <Casilla
          etiqueta="Solo con existencia"
          className="pt-3"
          checked={soloConExistencia}
          onChange={(e) => {
            setSoloConExistencia(e.target.checked);
            setPagina(1);
          }}
        />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(e) => `${e.almacenId}-${e.productoId}-${e.lote}`}
        mensajeVacio="No hay existencias con esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </Pagina>
  );
}

// ───────────────────────────── Kardex ─────────────────────────────

const DOCUMENTOS: DocumentoInventario[] = [
  'Recepcion',
  'OrdenProduccion',
  'Envio',
  'Venta',
  'Retorno',
  'Liquidacion',
  'Ajuste',
  'Traspaso',
  'ConteoFisico',
];

const ETIQUETAS_DOCUMENTO: Record<string, string> = {
  Recepcion: 'Recepción',
  OrdenProduccion: 'Orden de producción',
  Envio: 'Envío',
  Liquidacion: 'Liquidación',
  ConteoFisico: 'Conteo físico',
};

export function Kardex() {
  const hoy = fechaOperativa();
  const [producto, setProducto] = useState<ProductoDto | null>(null);
  const [almacenId, setAlmacenId] = useState('');
  const [desde, setDesde] = useState(sumarDias(hoy, -30));
  const [hasta, setHasta] = useState(hoy);
  const [documentoTipo, setDocumentoTipo] = useState('');
  const [pagina, setPagina] = useState(1);
  const { data, isLoading, isFetching } = useConsultarKardex({
    productoId: producto?.id,
    almacenId: almacenId || undefined,
    desde: desde || undefined,
    hasta: hasta || undefined,
    documentoTipo: (documentoTipo || undefined) as DocumentoInventario | undefined,
    pagina,
    tamano: 50,
  });

  const columnas = useMemo<ColumnDef<MovimientoDto, unknown>[]>(
    () => [
      { header: 'Fecha', accessorKey: 'fechaHora', cell: ({ row }) => formatoFechaHora(row.original.fechaHora) },
      { header: 'Movimiento', accessorKey: 'tipo', cell: ({ row }) => etiquetaMovimiento(row.original.tipo) },
      {
        header: 'Documento',
        accessorKey: 'documentoFolio',
        cell: ({ row }) => (
          <span>
            <span className="block font-medium">{row.original.documentoFolio}</span>
            <span className="text-xs text-muted">{ETIQUETAS_DOCUMENTO[row.original.documentoTipo] ?? etiquetaEnum(row.original.documentoTipo)}</span>
          </span>
        ),
      },
      { header: 'Almacén', accessorKey: 'almacenCodigo', meta: { ocultarEnMovil: true } },
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
      {
        header: 'Costo unitario',
        accessorKey: 'costoUnitario',
        cell: ({ row }) => formatoMoneda(row.original.costoUnitario),
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      {
        header: 'Saldo',
        accessorKey: 'existenciaResultante',
        cell: ({ row }) => <span className="font-medium">{formatoCantidad(row.original.existenciaResultante)}</span>,
        meta: { alinear: 'fin' },
      },
    ],
    [],
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Kardex"
        subtitulo="Movimientos inmutables de inventario; el saldo es la existencia del almacén y lote después de cada movimiento."
        acciones={
          <Boton
            variante="secundario"
            icono="download"
            disabled={!data?.items.length}
            onClick={() =>
              exportarCsv('kardex', data?.items ?? [], [
                { titulo: 'Fecha', valor: (m) => m.fechaHora },
                { titulo: 'Fecha operativa', valor: (m) => m.fechaOperativa },
                { titulo: 'Movimiento', valor: (m) => m.tipo },
                { titulo: 'Documento', valor: (m) => m.documentoFolio },
                { titulo: 'Almacén', valor: (m) => m.almacenCodigo },
                { titulo: 'SKU', valor: (m) => m.sku },
                { titulo: 'Lote', valor: (m) => m.lote },
                { titulo: 'Cantidad', valor: (m) => m.cantidad },
                { titulo: 'Costo unitario', valor: (m) => m.costoUnitario },
                { titulo: 'Costo total', valor: (m) => m.costoTotal },
                { titulo: 'Saldo', valor: (m) => m.existenciaResultante },
              ])
            }
          >
            Exportar CSV
          </Boton>
        }
      />
      <BarraFiltros>
        <SelectorProducto
          valor={producto?.id}
          producto={producto}
          onCambiar={(p) => {
            setProducto(p);
            setPagina(1);
          }}
        />
        <SelectorAlmacen incluirRutas vacio="Todos los almacenes" value={almacenId} onChange={(v) => (setAlmacenId(v), setPagina(1))} />
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => (setDesde(e.target.value), setPagina(1))} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => (setHasta(e.target.value), setPagina(1))} />
        <Selector
          etiqueta="Documento"
          vacio="Todos"
          opciones={opcionesEnum(DOCUMENTOS, ETIQUETAS_DOCUMENTO)}
          value={documentoTipo}
          onChange={(e) => (setDocumentoTipo(e.target.value), setPagina(1))}
        />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(m) => m.id}
        ordenable={false}
        mensajeVacio="No hay movimientos con esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </Pagina>
  );
}

// ───────────────────────────── Almacenes ─────────────────────────────

const ETIQUETAS_TIPO_ALMACEN: Record<string, string> = {
  MateriaPrima: 'Materia prima',
  ProductoTerminado: 'Producto terminado',
  ProductoSemiterminado: 'Producto semiterminado',
  Subalmacen: 'Subalmacén',
  Empaquetado: 'Empaquetado',
  General: 'General',
  Ruta: 'Ruta',
};

const TIPOS_ALMACEN = ['MateriaPrima', 'ProductoSemiterminado', 'ProductoTerminado', 'Empaquetado', 'Subalmacen', 'General'] as const;

const esquemaAlmacen = z.object({
  codigo: z.string().trim().min(1, 'Captura el código.').max(10, 'Máximo 10 caracteres.'),
  nombre: z.string().trim().min(1, 'Captura el nombre.'),
  tipo: z.enum(TIPOS_ALMACEN),
  activo: z.boolean(),
});
type DatosAlmacen = z.infer<typeof esquemaAlmacen>;

function DialogoAlmacen({ almacen, abierto, onCambiar }: { almacen: AlmacenDto | null; abierto: boolean; onCambiar: (a: boolean) => void }) {
  const crear = useCrearAlmacen();
  const actualizar = useActualizarAlmacen();
  const [error, setErrorGeneral] = useState<string | null>(null);
  const { register, handleSubmit, setError, formState } = useForm<DatosAlmacen>({
    resolver: zodResolver(esquemaAlmacen),
    values: almacen
      ? { codigo: almacen.codigo, nombre: almacen.nombre, tipo: almacen.tipo === 'Ruta' ? 'General' : almacen.tipo, activo: almacen.activo }
      : { codigo: '', nombre: '', tipo: 'General', activo: true },
  });
  const enviar = handleSubmit(async (d) => {
    setErrorGeneral(null);
    try {
      if (almacen) await actualizar.mutateAsync({ id: almacen.id, data: d });
      else await crear.mutateAsync({ data: d });
      toast.success('Almacén guardado.');
      onCambiar(false);
    } catch (e) {
      if (esErrorApi(e)) Object.entries(e.errores).forEach(([c, m]) => setError(c as Path<DatosAlmacen>, { message: m[0] }));
      setErrorGeneral(mensajeError(e));
    }
  });
  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo={almacen ? `Editar ${almacen.codigo}` : 'Nuevo almacén'}
      descripcion="Los almacenes de ruta se crean desde Logística junto con su ruta."
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
        <Campo etiqueta="Código" error={formState.errors.codigo?.message} {...register('codigo')} />
        <Campo etiqueta="Nombre" error={formState.errors.nombre?.message} {...register('nombre')} />
        <Selector
          etiqueta="Tipo"
          opciones={TIPOS_ALMACEN.map((t) => ({ valor: t, etiqueta: ETIQUETAS_TIPO_ALMACEN[t] }))}
          {...register('tipo')}
        />
        <Casilla etiqueta="Activo" className="pt-3" {...register('activo')} />
      </form>
    </Dialogo>
  );
}

export function Almacenes() {
  const { tienePermiso } = useSesion();
  const editable = tienePermiso('inventario.configurar');
  const { data, isLoading } = useListarAlmacenes();
  const [dialogo, setDialogo] = useState<{ abierto: boolean; almacen: AlmacenDto | null }>({ abierto: false, almacen: null });
  const columnas = useMemo<ColumnDef<AlmacenDto, unknown>[]>(
    () => [
      { header: 'Código', accessorKey: 'codigo', cell: ({ row }) => <span className="font-medium">{row.original.codigo}</span> },
      { header: 'Nombre', accessorKey: 'nombre' },
      { header: 'Tipo', accessorKey: 'tipo', cell: ({ row }) => ETIQUETAS_TIPO_ALMACEN[row.original.tipo] ?? row.original.tipo },
      {
        header: 'Negativos',
        accessorKey: 'permiteNegativo',
        cell: ({ row }) => (row.original.permiteNegativo ? 'Permitidos (ventas offline)' : 'No'),
        meta: { ocultarEnMovil: true },
      },
      { header: 'Estado', accessorKey: 'activo', cell: ({ row }) => <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} /> },
      {
        id: 'acciones',
        header: '',
        enableSorting: false,
        meta: { alinear: 'fin' },
        cell: ({ row }) =>
          editable && row.original.tipo !== 'Ruta' ? (
            <BotonIcono icono="edit" etiqueta={`Editar ${row.original.codigo}`} onClick={() => setDialogo({ abierto: true, almacen: row.original })} />
          ) : row.original.tipo === 'Ruta' ? (
            <span className="text-xs text-muted">Se administra en Logística</span>
          ) : null,
      },
    ],
    [editable],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Almacenes"
        subtitulo="Materias primas, producto terminado y un almacén por cada ruta."
        acciones={
          editable && (
            <Boton icono="add" onClick={() => setDialogo({ abierto: true, almacen: null })}>
              Nuevo almacén
            </Boton>
          )
        }
      />
      <TablaDatos columnas={columnas} datos={data} cargando={isLoading} idFila={(a) => a.id} />
      {dialogo.abierto && <DialogoAlmacen {...dialogo} onCambiar={(a) => setDialogo((d) => ({ ...d, abierto: a }))} />}
    </Pagina>
  );
}

// ───────────────────────────── Valuación ─────────────────────────────

export function Valuacion() {
  const [fecha, setFecha] = useState(fechaOperativa());
  const [incluirRutas, setIncluirRutas] = useState(true);
  const { data, isLoading, isFetching } = useConsultarValuacion({ fecha, incluirRutas });
  const columnas = useMemo<ColumnDef<ValuacionDto, unknown>[]>(
    () => [
      { header: 'SKU', accessorKey: 'sku', cell: ({ row }) => <span className="font-medium">{row.original.sku}</span> },
      { header: 'Producto', accessorKey: 'producto' },
      { header: 'Tipo', accessorKey: 'tipo', cell: ({ row }) => etiquetaEnum(row.original.tipo), meta: { ocultarEnMovil: true } },
      {
        header: 'Existencia',
        accessorKey: 'existencia',
        cell: ({ row }) => `${formatoCantidad(row.original.existencia)} ${row.original.unidad}`,
        meta: { alinear: 'fin' },
      },
      {
        header: 'Costo promedio',
        accessorKey: 'costoPromedio',
        cell: ({ row }) => formatoMoneda(row.original.costoPromedio),
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      { header: 'Valor', accessorKey: 'valor', cell: ({ row }) => <span className="font-medium">{formatoMoneda(row.original.valor)}</span>, meta: { alinear: 'fin' } },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Valuación de inventario"
        subtitulo="Existencia a la fecha por el costo promedio ponderado vigente [SUP-06]."
        acciones={
          <Boton
            variante="secundario"
            icono="download"
            disabled={!data?.productos.length}
            onClick={() =>
              exportarCsv(`valuacion-${fecha}`, data?.productos ?? [], [
                { titulo: 'SKU', valor: (v) => v.sku },
                { titulo: 'Producto', valor: (v) => v.producto },
                { titulo: 'Existencia', valor: (v) => v.existencia },
                { titulo: 'Unidad', valor: (v) => v.unidad },
                { titulo: 'Costo promedio', valor: (v) => v.costoPromedio },
                { titulo: 'Valor', valor: (v) => v.valor },
              ])
            }
          >
            Exportar CSV
          </Boton>
        }
      />
      <BarraFiltros>
        <Campo etiqueta="Fecha" type="date" value={fecha} max={fechaOperativa()} onChange={(e) => setFecha(e.target.value || fechaOperativa())} />
        <Casilla etiqueta="Incluir almacenes de ruta" className="pt-3" checked={incluirRutas} onChange={(e) => setIncluirRutas(e.target.checked)} />
      </BarraFiltros>
      <Tarjeta className="mb-4">
        <p className="m-0 text-sm text-muted">Valor total al {formatoFecha(data?.fecha ?? fecha)}</p>
        <p className="m-0 mt-1 text-3xl font-normal tabular-nums">{formatoMoneda(data?.total ?? 0)}</p>
      </Tarjeta>
      <TablaDatos
        columnas={columnas}
        datos={data?.productos}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(v) => v.productoId}
        mensajeVacio="No hay existencias a esa fecha."
      />
    </Pagina>
  );
}
