import { useListarClientesRuta, useListarParametros, useListarRutas, useListarVentas } from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFechaHora, formatoMoneda } from '@tg/shared';
import { Aviso, Campo, EstadoVacio, EtiquetaEstado, Icono, Tarjeta, TarjetaIndicador, cn } from '@tg/ui';
import { useEffect, useMemo, useState } from 'react';
import { BarraFiltros } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { DIAS_SEMANA, SelectorRuta, diaSemana } from '../logistica/comunes-logistica';
import { continuidadVisitas } from './continuidad';
import { MapaLeaflet, type LineaMapa, type MarcadorMapa } from './MapaLeaflet';

const COLOR_CLIENTE = '#005cbb';
const COLOR_ENTREGA = '#2e7d32';
const COLOR_REVISION = '#c62828';

function Leyenda({ color, texto }: { color: string; texto: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-13">
      <span className="inline-block size-3 rounded-full border-2 border-white shadow" style={{ background: color }} />
      {texto}
    </span>
  );
}

/**
 * Entregas del día por ruta (07-web): punto registrado del cliente vs punto de cada venta, con línea cuando la venta
 * quedó lejos (RN-VEN-05) y la continuidad de visitas según los días programados.
 */
export function MapaEntregas() {
  const [fecha, setFecha] = useState(fechaOperativa());
  const [rutaId, setRutaId] = useState('');
  const [seleccionado, setSeleccionado] = useState<string | null>(null);
  const rutas = useListarRutas({ activo: true });

  useEffect(() => {
    if (!rutaId && rutas.data?.length) setRutaId(rutas.data[0].id);
  }, [rutaId, rutas.data]);

  const clientes = useListarClientesRuta(rutaId, { query: { enabled: !!rutaId } });
  const ventas = useListarVentas({ desde: fecha, hasta: fecha, rutaId: rutaId || undefined, tamano: 200 }, { query: { enabled: !!rutaId } });
  const { data: parametros } = useListarParametros();
  const umbral = Number(parametros?.find((p) => p.clave === 'gps.umbral_distancia_m')?.valor ?? 150);
  const dia = diaSemana(fecha);

  const continuidad = useMemo(
    () => continuidadVisitas(clientes.data ?? [], ventas.data?.items ?? [], dia),
    [clientes.data, ventas.data, dia],
  );

  const { marcadores, lineas, sinGps } = useMemo(() => {
    const ms: MarcadorMapa[] = [];
    const ls: LineaMapa[] = [];
    const porId = new Map((clientes.data ?? []).map((c) => [c.clienteId, c]));
    for (const c of clientes.data ?? []) {
      if (c.latitud != null && c.longitud != null && !c.esGenerico) {
        ms.push({ id: `c-${c.clienteId}`, lat: c.latitud, lng: c.longitud, color: COLOR_CLIENTE, destacado: true, etiqueta: `${c.ordenVisita}. ${c.nombre} (registrado)` });
      }
    }
    let sin = 0;
    for (const v of ventas.data?.items ?? []) {
      if (v.estado === 'Cancelada') continue;
      if (v.latitud == null || v.longitud == null) {
        sin++;
        continue;
      }
      const color = v.requiereRevision ? COLOR_REVISION : COLOR_ENTREGA;
      ms.push({
        id: `v-${v.id}`,
        lat: v.latitud,
        lng: v.longitud,
        color,
        etiqueta: `${v.folio} · ${v.cliente} · ${formatoMoneda(v.total)}${v.distanciaClienteM != null ? ` · ${formatoCantidad(v.distanciaClienteM, 0)} m` : ''}`,
      });
      const c = porId.get(v.clienteId);
      if (c?.latitud != null && c.longitud != null && (v.distanciaClienteM ?? 0) > umbral) {
        ls.push({ id: v.id, desde: [c.latitud, c.longitud], hasta: [v.latitud, v.longitud], color: COLOR_REVISION, discontinua: true });
      }
    }
    return { marcadores: ms, lineas: ls, sinGps: sin };
  }, [clientes.data, ventas.data, umbral]);

  const ventasValidas = (ventas.data?.items ?? []).filter((v) => v.estado !== 'Cancelada');
  const revision = ventasValidas.filter((v) => v.requiereRevision).length;

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Mapa de entregas"
        subtitulo="Punto registrado de cada cliente contra el punto donde se registró la venta, y continuidad de visitas."
        migas={[{ etiqueta: 'General' }, { etiqueta: 'Mapa de entregas' }]}
      />
      <BarraFiltros>
        <Campo etiqueta="Fecha operativa" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
        <SelectorRuta value={rutaId} onChange={setRutaId} vacio="Elige la ruta" />
      </BarraFiltros>
      {!rutaId ? (
        <EstadoVacio icono="route" mensaje="Elige una ruta para ver sus entregas." />
      ) : (
        <div className="flex flex-col gap-4">
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <TarjetaIndicador icono="receipt" valor={ventasValidas.length} titulo="Ventas del día" nota={formatoMoneda(ventasValidas.reduce((a, v) => a + v.total, 0))} />
            <TarjetaIndicador
              icono="where_to_vote"
              valor={`${continuidad.visitadosProgramados}/${continuidad.programados}`}
              titulo="Visitas programadas"
              nota={DIAS_SEMANA.find((d) => d.valor === dia)?.nombre}
              tono={continuidad.noVisitados.length > 0 ? 'alerta' : 'bien'}
            />
            <TarjetaIndicador icono="wrong_location" valor={revision} titulo="Requieren revisión" nota={`Más de ${umbral} m del cliente`} tono={revision > 0 ? 'alerta' : 'normal'} />
            <TarjetaIndicador icono="location_off" valor={sinGps} titulo="Ventas sin GPS" />
          </div>
          {sinGps > 0 && <Aviso tipo="advertencia">{sinGps} venta(s) se registraron sin GPS y no aparecen en el mapa.</Aviso>}
          <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_459px]">
            <div>
              <MapaLeaflet marcadores={marcadores} lineas={lineas} alSeleccionar={setSeleccionado} />
              <div className="mt-2 flex flex-wrap gap-4 px-1 text-muted">
                <Leyenda color={COLOR_CLIENTE} texto="Cliente (ubicación aprobada)" />
                <Leyenda color={COLOR_ENTREGA} texto="Venta dentro del rango" />
                <Leyenda color={COLOR_REVISION} texto="Venta que requiere revisión" />
              </div>
            </div>
            <Tarjeta
              titulo="Continuidad de visitas"
              subtitulo={continuidad.fueraDePrograma > 0 ? `${continuidad.fueraDePrograma} venta(s) a clientes fuera de su día.` : undefined}
              className="max-h-[560px] overflow-y-auto"
            >
              <ol className="m-0 flex list-none flex-col gap-1 p-0">
                {continuidad.visitas.map((v) => {
                  const activo = seleccionado === `c-${v.cliente.clienteId}` || v.ventas.some((x) => seleccionado === `v-${x.id}`);
                  return (
                    <li
                      key={v.cliente.clienteId}
                      className={cn('flex items-start gap-2 rounded-lg px-2 py-2', activo && 'bg-row-hover', !v.tocaHoy && !v.visitado && 'opacity-60')}
                    >
                      <Icono
                        nombre={v.visitado ? 'check_circle' : v.tocaHoy ? 'cancel' : 'radio_button_unchecked'}
                        tamano={20}
                        className={v.visitado ? 'text-[var(--tg-status-green-fg)]' : v.tocaHoy ? 'text-[var(--tg-status-red-fg)]' : 'text-muted'}
                      />
                      <div className="min-w-0 flex-1">
                        <span className="block text-sm font-medium">
                          {v.cliente.ordenVisita}. {v.cliente.nombre}
                        </span>
                        <span className="text-xs text-muted">
                          {v.cliente.esGenerico
                            ? 'Público en general'
                            : v.tocaHoy
                              ? v.visitado
                                ? v.ventas.map((x) => `${x.folio} ${formatoFechaHora(x.fechaHoraDispositivo ?? x.fechaHoraServidor).slice(11)}`).join(' · ')
                                : 'No visitado'
                              : v.visitado
                                ? 'Visitado fuera de su día'
                                : 'No le toca hoy'}
                        </span>
                      </div>
                      {v.ventas.some((x) => x.requiereRevision) && <EtiquetaEstado etiqueta="Revisar" color="rojo" />}
                    </li>
                  );
                })}
                {continuidad.visitas.length === 0 && <li className="text-sm text-muted">La ruta no tiene clientes.</li>}
              </ol>
            </Tarjeta>
          </div>
        </div>
      )}
    </Pagina>
  );
}
