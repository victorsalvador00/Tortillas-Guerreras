import { useNavigate } from '@tanstack/react-router';
import { useListarProductos, type ProductoDto, type TipoProducto } from '@tg/api-client';
import { formatoCantidad, formatoPorcentaje } from '@tg/shared';
import { Boton, Campo, EtiquetaEstado, Selector, TablaDatos, etiquetaEnum, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros, exportarCsv } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';

export const TIPOS_PRODUCTO: { valor: TipoProducto; etiqueta: string }[] = [
  { valor: 'MateriaPrima', etiqueta: 'Materia prima' },
  { valor: 'Empaque', etiqueta: 'Empaque' },
  { valor: 'Semiterminado', etiqueta: 'Semiterminado' },
  { valor: 'Terminado', etiqueta: 'Terminado' },
  { valor: 'Reventa', etiqueta: 'Reventa' },
];

function Bandera({ activa, texto }: { activa: boolean; texto: string }) {
  if (!activa) return null;
  return <span className="rounded-full bg-soft px-2.5 py-0.5 text-xs font-medium text-primary">{texto}</span>;
}

export function Productos() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const [buscar, setBuscar] = useState('');
  const [tipo, setTipo] = useState('');
  const [activo, setActivo] = useState('true');
  const [pagina, setPagina] = useState(1);
  const params = {
    buscar: buscar || undefined,
    tipo: (tipo || undefined) as TipoProducto | undefined,
    activo: activo === '' ? undefined : activo === 'true',
    pagina,
    tamano: 50,
  };
  const { data, isLoading, isFetching } = useListarProductos(params);

  const columnas = useMemo<ColumnDef<ProductoDto, unknown>[]>(
    () => [
      { header: 'SKU', accessorKey: 'sku', cell: ({ row }) => <span className="font-medium whitespace-nowrap">{row.original.sku}</span> },
      { header: 'Nombre', accessorKey: 'nombre' },
      { header: 'Tipo', accessorKey: 'tipo', cell: ({ row }) => etiquetaEnum(row.original.tipo), meta: { ocultarEnMovil: true } },
      { header: 'Unidad', accessorKey: 'unidadBaseClave', meta: { ocultarEnMovil: true } },
      {
        header: 'Uso',
        id: 'uso',
        enableSorting: false,
        cell: ({ row }) => (
          <span className="flex flex-wrap gap-1">
            <Bandera activa={row.original.esComprable} texto="Compra" />
            <Bandera activa={row.original.esProducible} texto="Producción" />
            <Bandera activa={row.original.esVendible} texto="Venta" />
            <Bandera activa={row.original.manejaLote} texto="Lote" />
          </span>
        ),
        meta: { ocultarEnMovil: true },
      },
      {
        header: 'Impuestos',
        id: 'impuestos',
        cell: ({ row }) =>
          `IVA ${formatoPorcentaje(row.original.tasaIva)}${row.original.tasaIeps ? ` · IEPS ${formatoPorcentaje(row.original.tasaIeps)}` : ''}`,
        meta: { ocultarEnMovil: true },
      },
      {
        header: 'Mínimo',
        accessorKey: 'stockMinimo',
        cell: ({ row }) => (row.original.stockMinimo != null ? formatoCantidad(row.original.stockMinimo) : '—'),
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      {
        header: 'Estado',
        accessorKey: 'activo',
        cell: ({ row }) => <EtiquetaEstado estado={row.original.activo ? 'Activo' : 'Inactivo'} />,
      },
    ],
    [],
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Productos"
        subtitulo="Materias primas, empaques, semiterminados, terminados y reventa con sus unidades de compra y venta."
        acciones={
          <>
            <Boton
              variante="secundario"
              icono="download"
              disabled={!data?.items.length}
              onClick={() =>
                exportarCsv('productos', data?.items ?? [], [
                  { titulo: 'SKU', valor: (p) => p.sku },
                  { titulo: 'Nombre', valor: (p) => p.nombre },
                  { titulo: 'Tipo', valor: (p) => p.tipo },
                  { titulo: 'Unidad base', valor: (p) => p.unidadBaseClave },
                  { titulo: 'IVA', valor: (p) => p.tasaIva },
                  { titulo: 'IEPS', valor: (p) => p.tasaIeps ?? '' },
                  { titulo: 'Clave SAT', valor: (p) => p.claveProdServSat ?? '' },
                  { titulo: 'Activo', valor: (p) => (p.activo ? 'Sí' : 'No') },
                ])
              }
            >
              Exportar CSV
            </Boton>
            {tienePermiso('catalogos.editar') && (
              <Boton icono="add" onClick={() => navegar({ to: '/catalogos/productos/nuevo' })}>
                Nuevo producto
              </Boton>
            )}
          </>
        }
      />
      <BarraFiltros>
        <Campo
          etiqueta="Buscar por SKU, nombre o código de barras"
          type="search"
          value={buscar}
          onChange={(e) => {
            setBuscar(e.target.value);
            setPagina(1);
          }}
          sufijo={<span className="icono text-22">search</span>}
        />
        <Selector
          etiqueta="Tipo"
          vacio="Todos"
          opciones={TIPOS_PRODUCTO}
          value={tipo}
          onChange={(e) => {
            setTipo(e.target.value);
            setPagina(1);
          }}
        />
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={[
            { valor: 'true', etiqueta: 'Activos' },
            { valor: 'false', etiqueta: 'Inactivos' },
          ]}
          value={activo}
          onChange={(e) => {
            setActivo(e.target.value);
            setPagina(1);
          }}
        />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(p) => p.id}
        alClicFila={(p) => navegar({ to: '/catalogos/productos/$id', params: { id: p.id } })}
        mensajeVacio="No hay productos con esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </Pagina>
  );
}
