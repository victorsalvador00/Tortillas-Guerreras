import { listarClientes, useListarAlmacenes, type ClienteDto } from '@tg/api-client';
import { BuscadorSeleccion, Selector, type OpcionBusqueda } from '@tg/ui';
import { useCallback, useMemo } from 'react';

/** Cliente con búsqueda en el API (código, nombre o RFC). */
export function SelectorCliente({
  valor,
  cliente,
  onCambiar,
  etiqueta = 'Cliente',
  error,
  soloCredito,
  className,
  deshabilitado,
}: {
  valor: string | null | undefined;
  cliente?: Pick<ClienteDto, 'id' | 'codigo' | 'nombre'> | null;
  onCambiar: (cliente: ClienteDto | null) => void;
  etiqueta?: string;
  error?: string;
  soloCredito?: boolean;
  className?: string;
  deshabilitado?: boolean;
}) {
  const cache = useMemo(() => new Map<string, ClienteDto>(), []);
  const buscar = useCallback(
    async (texto: string): Promise<OpcionBusqueda[]> => {
      const r = await listarClientes({ buscar: texto || undefined, activo: true, tamano: 30 });
      const items = soloCredito ? r.items.filter((c) => c.condicionPago === 'Credito') : r.items;
      items.forEach((c) => cache.set(c.id, c));
      return items.map((c) => ({ valor: c.id, etiqueta: `${c.codigo} · ${c.nombre}`, detalle: `${c.canal} · ${c.condicionPago === 'Credito' ? 'Crédito' : 'Contado'}` }));
    },
    [cache, soloCredito],
  );
  return (
    <BuscadorSeleccion
      etiqueta={etiqueta}
      valor={valor}
      seleccion={cliente ? { valor: cliente.id, etiqueta: cliente.codigo ? `${cliente.codigo} · ${cliente.nombre}` : cliente.nombre } : null}
      buscar={buscar}
      onCambiar={(id) => onCambiar(id ? (cache.get(id) ?? null) : null)}
      error={error}
      className={className}
      deshabilitado={deshabilitado}
    />
  );
}

/** Rutas a partir de sus almacenes tipo Ruta (el almacén guarda el id de la ruta, RN-LOG-01). */
export function useRutas(): { rutaId: string; almacenId: string; codigo: string; nombre: string }[] {
  const { data } = useListarAlmacenes({ tipo: 'Ruta' });
  return (data ?? [])
    .filter((a) => a.rutaId)
    .map((a) => ({ rutaId: a.rutaId!, almacenId: a.id, codigo: a.codigo, nombre: a.nombre }));
}

export function SelectorRuta({
  value,
  onChange,
  etiqueta = 'Ruta',
  vacio,
  error,
  className,
}: {
  value: string;
  onChange: (rutaId: string) => void;
  etiqueta?: string;
  vacio?: string;
  error?: string;
  className?: string;
}) {
  const rutas = useRutas();
  return (
    <Selector
      etiqueta={etiqueta}
      vacio={vacio}
      opciones={rutas.map((r) => ({ valor: r.rutaId, etiqueta: `${r.codigo} · ${r.nombre}` }))}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      error={error}
      className={className}
    />
  );
}

export const ETIQUETAS_FORMA_PAGO: Record<string, string> = {
  Efectivo: 'Efectivo',
  Transferencia: 'Transferencia',
  Credito: 'Crédito',
};

export const ETIQUETAS_INCIDENCIA: Record<string, string> = {
  ExistenciaNegativaRuta: 'Existencia negativa en ruta',
  GpsFueraDeRango: 'GPS fuera de rango',
  GpsImpreciso: 'GPS impreciso',
  SinGps: 'Sin GPS',
  PrecioDistinto: 'Precio distinto',
  UbicacionPorAprobar: 'Ubicación por aprobar',
  DiferenciaLiquidacion: 'Diferencia en liquidación',
  CreditoExcedido: 'Crédito excedido',
  TotalDistinto: 'Total distinto',
};
