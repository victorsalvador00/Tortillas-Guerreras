import type { AnyRoute } from '@tanstack/react-router';
import { pantalla } from '@/app/rutas-base';
import { CambiosPendientes } from './CambiosPendientes';
import { CargaDetalle, CargaNueva, Cargas } from './Cargas';
import { Rutas, Vehiculos } from './Configuracion';
import { CorteDetalle, CorteSemanal } from './CorteSemanal';
import { CuentaChoferes } from './CuentaChoferes';
import { HojaLiquidacion } from './HojaLiquidacion';
import { LiquidacionDetalle, Liquidaciones } from './Liquidaciones';
import { RetornoNuevo, Retornos } from './Retornos';
import { RutaDetalle } from './RutaDetalle';

export function rutasLogistica(): AnyRoute[] {
  return [
    pantalla('/logistica/cargas', Cargas, 'logistica.ver'),
    pantalla('/logistica/cargas/nueva', CargaNueva, 'logistica.cargar'),
    pantalla('/logistica/cargas/$id', CargaDetalle, 'logistica.ver'),
    pantalla('/logistica/liquidaciones', Liquidaciones, 'logistica.ver'),
    pantalla('/logistica/liquidaciones/$id', LiquidacionDetalle, 'logistica.ver'),
    pantalla('/logistica/liquidaciones/$id/hoja', HojaLiquidacion, 'logistica.ver'),
    pantalla('/logistica/retornos', Retornos, ['logistica.retornos', 'logistica.ver']),
    pantalla('/logistica/retornos/nuevo', RetornoNuevo, 'logistica.retornos'),
    pantalla('/logistica/cambios-pendientes', CambiosPendientes, 'logistica.ver'),
    pantalla('/logistica/cuenta-choferes', CuentaChoferes, 'logistica.cargos_chofer'),
    pantalla('/logistica/cortes', CorteSemanal, 'logistica.cargos_chofer'),
    pantalla('/logistica/cortes/$id', CorteDetalle, 'logistica.cargos_chofer'),
    pantalla('/logistica/rutas', Rutas, 'logistica.configurar'),
    pantalla('/logistica/rutas/$id', RutaDetalle, 'logistica.configurar'),
    pantalla('/logistica/vehiculos', Vehiculos, 'logistica.configurar'),
  ];
}
