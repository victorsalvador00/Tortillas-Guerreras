import { useEffect, useRef } from 'react';

interface Nodo {
  x: number;
  y: number;
  vx: number;
  vy: number;
  r: number;
}

/** Píxeles por nodo: la cantidad se ajusta al tamaño (menos nodos en celular). */
const AREA_POR_NODO = 8_000;
const MIN_NODOS = 28;
const MAX_NODOS = 140;
/** Distancia máxima (px CSS) para unir dos nodos con una línea. */
const DISTANCIA_UNION = 140;
const VELOCIDAD = 0.25;

const PALETA = {
  claro: { linea: '226, 200, 255', nodo: '#f6ecff' },
  oscuro: { linea: '140, 165, 255', nodo: '#e3e8ff' },
};

/**
 * Fondo decorativo de red "plexus" del inicio de sesión: nodos que flotan despacio unidos por líneas cuando están
 * cerca. Se detiene con "reducir movimiento" (queda un cuadro fijo) y sigue el tema. Decorativo: `aria-hidden`.
 */
export function Plexus({ oscuro }: { oscuro: boolean }) {
  const lienzo = useRef<HTMLCanvasElement>(null);
  const tema = useRef(oscuro);
  const redibujar = useRef<(() => void) | null>(null);

  useEffect(() => {
    tema.current = oscuro;
    redibujar.current?.();
  }, [oscuro]);

  useEffect(() => {
    const canvas = lienzo.current;
    const contexto = canvas?.getContext?.('2d');
    if (!canvas || !contexto) return;
    const quieto = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    const nodos: Nodo[] = [];
    let ancho = 0;
    let alto = 0;
    let cuadro = 0;

    const sembrar = () => {
      const cantidad = Math.max(MIN_NODOS, Math.min(MAX_NODOS, Math.round((ancho * alto) / AREA_POR_NODO)));
      nodos.length = Math.min(nodos.length, cantidad);
      for (const n of nodos) {
        n.x = Math.min(n.x, ancho);
        n.y = Math.min(n.y, alto);
      }
      while (nodos.length < cantidad) {
        nodos.push({
          x: Math.random() * ancho,
          y: Math.random() * alto,
          vx: (Math.random() - 0.5) * 2 * VELOCIDAD,
          vy: (Math.random() - 0.5) * 2 * VELOCIDAD,
          r: 1.2 + Math.random() * 1.6,
        });
      }
    };

    const dibujar = () => {
      const colores = tema.current ? PALETA.oscuro : PALETA.claro;
      contexto.clearRect(0, 0, ancho, alto);
      contexto.lineWidth = 1;
      for (let i = 0; i < nodos.length; i++) {
        const a = nodos[i];
        for (let j = i + 1; j < nodos.length; j++) {
          const b = nodos[j];
          const d = Math.hypot(a.x - b.x, a.y - b.y);
          if (d < DISTANCIA_UNION) {
            contexto.strokeStyle = `rgba(${colores.linea}, ${(1 - d / DISTANCIA_UNION) * 0.7})`;
            contexto.beginPath();
            contexto.moveTo(a.x, a.y);
            contexto.lineTo(b.x, b.y);
            contexto.stroke();
          }
        }
      }
      contexto.fillStyle = colores.nodo;
      for (const n of nodos) {
        contexto.beginPath();
        contexto.arc(n.x, n.y, n.r, 0, Math.PI * 2);
        contexto.fill();
      }
    };

    const redimensionar = () => {
      const ratio = Math.min(window.devicePixelRatio || 1, 2);
      ancho = canvas.clientWidth;
      alto = canvas.clientHeight;
      canvas.width = Math.round(ancho * ratio);
      canvas.height = Math.round(alto * ratio);
      contexto.setTransform(ratio, 0, 0, ratio, 0, 0);
      sembrar();
      if (quieto) dibujar();
    };

    const observador = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(redimensionar) : null;
    observador?.observe(canvas);
    redimensionar();
    redibujar.current = quieto ? dibujar : null;

    if (!quieto) {
      const ciclo = () => {
        for (const n of nodos) {
          n.x += n.vx;
          n.y += n.vy;
          if (n.x < 0 || n.x > ancho) n.vx *= -1;
          if (n.y < 0 || n.y > alto) n.vy *= -1;
        }
        dibujar();
        cuadro = requestAnimationFrame(ciclo);
      };
      cuadro = requestAnimationFrame(ciclo);
    }

    return () => {
      cancelAnimationFrame(cuadro);
      observador?.disconnect();
    };
  }, []);

  return (
    <div aria-hidden className="pointer-events-none absolute inset-0">
      <canvas ref={lienzo} className="block size-full" />
    </div>
  );
}
