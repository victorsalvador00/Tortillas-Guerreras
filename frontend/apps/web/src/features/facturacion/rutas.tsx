import type { AnyRoute } from '@tanstack/react-router';
import { pantalla } from '@/app/rutas-base';
import { FacturaDetalle, FacturaNueva, Facturas } from './Facturas';
import { Emisor, FacturaGlobal, Solicitudes } from './Otras';

export function rutasFacturacion(): AnyRoute[] {
  return [
    pantalla('/facturacion/facturas', Facturas, 'facturacion.ver'),
    pantalla('/facturacion/facturas/nueva', FacturaNueva, 'facturacion.emitir'),
    pantalla('/facturacion/facturas/$id', FacturaDetalle, 'facturacion.ver'),
    pantalla('/facturacion/solicitudes', Solicitudes, 'facturacion.ver'),
    pantalla('/facturacion/global', FacturaGlobal, 'facturacion.emitir'),
    pantalla('/facturacion/emisor', Emisor, ['facturacion.configurar', 'facturacion.ver']),
  ];
}
