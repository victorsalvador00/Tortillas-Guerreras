import L from 'leaflet';
import { useEffect, useRef } from 'react';

export interface MarcadorMapa {
  id: string;
  lat: number;
  lng: number;
  /** Color de relleno del círculo. */
  color: string;
  /** Borde más grueso y radio mayor (p. ej. el punto del cliente). */
  destacado?: boolean;
  etiqueta: string;
}

export interface LineaMapa {
  id: string;
  desde: [number, number];
  hasta: [number, number];
  color: string;
  discontinua?: boolean;
}

/** Centro por defecto: Veracruz (zona de las rutas de ejemplo). */
const CENTRO: [number, number] = [19.18, -96.14];

/**
 * Mapa con Leaflet directo y mosaicos de OpenStreetMap (sin react-leaflet por licencia). Los puntos son círculos
 * vectoriales (no requieren las imágenes de íconos de Leaflet) y la vista se ajusta a lo que se muestra.
 */
export function MapaLeaflet({
  marcadores,
  lineas = [],
  alto = 480,
  alSeleccionar,
}: {
  marcadores: MarcadorMapa[];
  lineas?: LineaMapa[];
  alto?: number;
  alSeleccionar?: (id: string) => void;
}) {
  const contenedor = useRef<HTMLDivElement>(null);
  const mapa = useRef<L.Map | null>(null);
  const capa = useRef<L.LayerGroup | null>(null);
  const seleccionar = useRef(alSeleccionar);
  seleccionar.current = alSeleccionar;

  useEffect(() => {
    if (!contenedor.current || mapa.current) return;
    const m = L.map(contenedor.current, { center: CENTRO, zoom: 13, scrollWheelZoom: true });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; colaboradores de OpenStreetMap',
    }).addTo(m);
    capa.current = L.layerGroup().addTo(m);
    mapa.current = m;
    return () => {
      m.remove();
      mapa.current = null;
      capa.current = null;
    };
  }, []);

  useEffect(() => {
    const m = mapa.current;
    const grupo = capa.current;
    if (!m || !grupo) return;
    grupo.clearLayers();
    for (const l of lineas) {
      L.polyline([l.desde, l.hasta], { color: l.color, weight: 2, dashArray: l.discontinua ? '6 6' : undefined }).addTo(grupo);
    }
    const puntos: L.LatLngExpression[] = [];
    for (const mk of marcadores) {
      const c = L.circleMarker([mk.lat, mk.lng], {
        radius: mk.destacado ? 9 : 7,
        color: '#ffffff',
        weight: mk.destacado ? 3 : 2,
        fillColor: mk.color,
        fillOpacity: 0.95,
      })
        .bindTooltip(mk.etiqueta)
        .addTo(grupo);
      c.on('click', () => seleccionar.current?.(mk.id));
      puntos.push([mk.lat, mk.lng]);
    }
    if (puntos.length > 0) {
      m.fitBounds(L.latLngBounds(puntos), { padding: [32, 32], maxZoom: 16 });
    }
  }, [marcadores, lineas]);

  return (
    <div
      ref={contenedor}
      role="region"
      aria-label="Mapa de entregas"
      className="z-0 overflow-hidden rounded-card border border-card-border shadow-card"
      style={{ height: alto }}
    />
  );
}
