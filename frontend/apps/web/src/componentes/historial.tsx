import { useListarBitacora } from '@tg/api-client';
import { formatoFechaHora } from '@tg/shared';
import { Historial } from '@tg/ui';

/** Nombre legible de las tablas con bitácora (RN-SEG-06). */
export const TABLAS: Record<string, string> = {
  productos: 'Producto',
  productos_unidades: 'Conversión de producto',
  clientes: 'Cliente',
  listas_precio: 'Lista de precios',
  listas_precio_partidas: 'Precio de lista',
  pedidos: 'Pedido',
  pedidos_partidas: 'Partida de pedido',
  rutas: 'Ruta',
  rutas_clientes: 'Cliente de ruta',
  envios: 'Carga',
  envios_partidas: 'Partida de carga',
  retornos: 'Retorno',
  retornos_partidas: 'Partida de retorno',
  liquidaciones: 'Liquidación',
  liquidaciones_partidas: 'Partida de liquidación',
  usuarios: 'Usuario',
  dispositivos: 'Dispositivo',
};

export const etiquetaTabla = (tabla: string) => TABLAS[tabla] ?? tabla;

/** Pestaña "Historial" de una pantalla de detalle: el documento y sus partidas (RN-SEG-07). */
export function HistorialDocumento({ tabla, documentoId }: { tabla: string; documentoId: string }) {
  const { data, isLoading } = useListarBitacora({ tabla, documentoId, tamano: 200 });
  return <Historial registros={data?.items} cargando={isLoading} formatoFecha={formatoFechaHora} etiquetaTabla={etiquetaTabla} />;
}
