import L from 'leaflet';
import iconoRetina from 'leaflet/dist/images/marker-icon-2x.png';
import icono from 'leaflet/dist/images/marker-icon.png';
import sombra from 'leaflet/dist/images/marker-shadow.png';
import { cn } from '@tg/ui';
import { useEffect, useRef } from 'react';

// Vite empaqueta las imágenes: se reemplazan las rutas que Leaflet calcula por su cuenta.
L.Icon.Default.mergeOptions({ iconRetinaUrl: iconoRetina, iconUrl: icono, shadowUrl: sombra });

export type ColorMarcador = 'azul' | 'verde' | 'naranja' | 'rojo' | 'gris';

const COLORES: Record<ColorMarcador, string> = {
  azul: '#005cbb',
  verde: '#2e7d32',
  naranja: '#e65100',
  rojo: '#c62828',
  gris: '#607d8b',
};

export interface MarcadorMapa {
  id: string;
  lat: number;
  lng: number;
  titulo: string;
  /** Texto (o HTML simple escapado) del globo al hacer clic. */
  detalle?: string;
  /** Sin color se usa el marcador estándar de Leaflet. */
  color?: ColorMarcador;
}

export interface LineaMapa {
  puntos: [number, number][];
  color?: ColorMarcador;
  discontinua?: boolean;
}

function escapar(texto: string): string {
  return texto.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
}

function iconoColor(color: ColorMarcador): L.DivIcon {
  return L.divIcon({
    className: '',
    iconSize: [22, 22],
    iconAnchor: [11, 11],
    popupAnchor: [0, -10],
    html: `<span style="display:block;width:22px;height:22px;border-radius:50%;background:${COLORES[color]};border:3px solid #fff;box-shadow:0 1px 4px rgb(0 0 0/45%)"></span>`,
  });
}

/**
 * Mapa con Leaflet y mosaicos de OpenStreetMap (D-09). Muestra marcadores y líneas y ajusta la vista para que
 * quepan todos. Se usa en la ficha del cliente y en el mapa de entregas.
 */
export function MapaLeaflet({
  marcadores,
  lineas = [],
  centro = [19.18, -96.14],
  zoom = 13,
  className,
  alClicMapa,
}: {
  marcadores: MarcadorMapa[];
  lineas?: LineaMapa[];
  centro?: [number, number];
  zoom?: number;
  className?: string;
  /** Si se indica, un clic en el mapa entrega la coordenada (p. ej. para fijar la ubicación). */
  alClicMapa?: (lat: number, lng: number) => void;
}) {
  const contenedor = useRef<HTMLDivElement>(null);
  const mapa = useRef<L.Map | null>(null);
  const capa = useRef<L.LayerGroup | null>(null);
  const clic = useRef(alClicMapa);
  clic.current = alClicMapa;

  useEffect(() => {
    if (!contenedor.current) return;
    const m = L.map(contenedor.current, { center: centro, zoom, scrollWheelZoom: false });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; colaboradores de OpenStreetMap',
    }).addTo(m);
    capa.current = L.layerGroup().addTo(m);
    m.on('click', (e: L.LeafletMouseEvent) => clic.current?.(e.latlng.lat, e.latlng.lng));
    mapa.current = m;
    return () => {
      m.remove();
      mapa.current = null;
    };
    // El mapa se crea una sola vez; centro y zoom iniciales.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const firma = JSON.stringify([marcadores, lineas]);
  useEffect(() => {
    const m = mapa.current;
    const grupo = capa.current;
    if (!m || !grupo) return;
    grupo.clearLayers();
    const limites: L.LatLngExpression[] = [];
    for (const l of lineas) {
      L.polyline(l.puntos, {
        color: COLORES[l.color ?? 'azul'],
        weight: 3,
        dashArray: l.discontinua ? '6 6' : undefined,
      }).addTo(grupo);
      limites.push(...l.puntos);
    }
    for (const mk of marcadores) {
      const marcador = L.marker([mk.lat, mk.lng], {
        title: mk.titulo,
        ...(mk.color ? { icon: iconoColor(mk.color) } : {}),
      });
      marcador.bindPopup(`<strong>${escapar(mk.titulo)}</strong>${mk.detalle ? `<br/>${escapar(mk.detalle)}` : ''}`);
      marcador.addTo(grupo);
      limites.push([mk.lat, mk.lng]);
    }
    if (limites.length === 1) {
      m.setView(limites[0], Math.max(m.getZoom(), 15));
    } else if (limites.length > 1) {
      m.fitBounds(L.latLngBounds(limites), { padding: [32, 32], maxZoom: 17 });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [firma]);

  return (
    <div
      ref={contenedor}
      role="region"
      aria-label="Mapa"
      className={cn('h-80 w-full overflow-hidden rounded-card border border-card-border', className)}
    />
  );
}
