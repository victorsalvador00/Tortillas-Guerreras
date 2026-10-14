import type { ClienteRutaDto, VentaDto } from '@tg/api-client';

export interface VisitaCliente {
  cliente: ClienteRutaDto;
  /** El cliente se visita el día de la semana consultado (`dias_visita`). */
  tocaHoy: boolean;
  ventas: VentaDto[];
  visitado: boolean;
}

export interface Continuidad {
  visitas: VisitaCliente[];
  programados: number;
  visitadosProgramados: number;
  noVisitados: VisitaCliente[];
  /** Ventas a clientes que no tocaba visitar ese día. */
  fueraDePrograma: number;
}

/**
 * Continuidad de visitas (07-web): por cada cliente de la ruta, si tocaba visitarlo ese día y si tuvo al menos una
 * venta registrada. El cliente genérico no cuenta como programado.
 */
export function continuidadVisitas(clientes: ClienteRutaDto[], ventas: VentaDto[], dia: number): Continuidad {
  const porCliente = new Map<string, VentaDto[]>();
  for (const v of ventas) {
    if (v.estado === 'Cancelada') continue;
    porCliente.set(v.clienteId, [...(porCliente.get(v.clienteId) ?? []), v]);
  }
  const visitas = [...clientes]
    .sort((a, b) => a.ordenVisita - b.ordenVisita)
    .map((c) => {
      const vs = porCliente.get(c.clienteId) ?? [];
      return { cliente: c, tocaHoy: !c.esGenerico && c.diasVisita.includes(dia), ventas: vs, visitado: vs.length > 0 };
    });
  const programados = visitas.filter((v) => v.tocaHoy);
  return {
    visitas,
    programados: programados.length,
    visitadosProgramados: programados.filter((v) => v.visitado).length,
    noVisitados: programados.filter((v) => !v.visitado),
    fueraDePrograma: visitas.filter((v) => !v.tocaHoy && !v.cliente.esGenerico && v.visitado).length,
  };
}
