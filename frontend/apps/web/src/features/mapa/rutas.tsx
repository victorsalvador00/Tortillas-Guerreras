import type { AnyRoute } from '@tanstack/react-router';
import { pantalla } from '@/app/rutas-base';
import { MapaEntregas } from './MapaEntregas';

export function rutasMapa(): AnyRoute[] {
  return [pantalla('/mapa', MapaEntregas, ['ventas.ver', 'logistica.ver'])];
}
