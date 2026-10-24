import type { AnyRoute } from '@tanstack/react-router';
import { pantalla } from '@/app/rutas-base';
import { Bitacora } from './Bitacora';
import { Dispositivos } from './Dispositivos';
import { Roles } from './Roles';
import { Usuarios } from './Usuarios';

export function rutasSeguridad(): AnyRoute[] {
  return [
    pantalla('/seguridad/usuarios', Usuarios, 'seguridad.usuarios'),
    pantalla('/seguridad/roles', Roles, 'seguridad.roles'),
    pantalla('/seguridad/dispositivos', Dispositivos, 'seguridad.dispositivos'),
    pantalla('/seguridad/bitacora', Bitacora, 'seguridad.bitacora'),
  ];
}
