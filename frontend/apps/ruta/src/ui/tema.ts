import { useColorScheme } from 'react-native';

/** Mismos tokens que la web (`@tg/ui/tema.css`): azul SGO en claro y azul neón en oscuro. */
const claro = {
  primario: '#005cbb',
  sobrePrimario: '#ffffff',
  fondo: '#f3f4f9',
  tarjeta: '#ffffff',
  borde: 'transparent',
  texto: '#1a1b1f',
  textoSuave: '#44474e',
  divisor: '#eceef5',
  suave: '#eceff7',
  campo: '#ffffff',
  campoBorde: '#dfe3ee',
  barra: '#005cbb',
  barraTexto: '#ffffff',
  error: '#ba1a1a',
  sombra: 'rgba(35, 40, 80, 0.10)',
  estado: {
    gris: ['#eceff1', '#37474f'],
    azul: ['#e3f2fd', '#0d47a1'],
    amarillo: ['#fff8e1', '#7a5a00'],
    verde: ['#e8f5e9', '#1b5e20'],
    naranja: ['#fff3e0', '#a64200'],
    rojo: ['#ffebee', '#b71c1c'],
  },
};

const oscuro: typeof claro = {
  primario: '#4f86ff',
  sobrePrimario: '#000000',
  fondo: '#0d0f14',
  tarjeta: '#14171e',
  borde: '#44474e',
  texto: '#e3e2e6',
  textoSuave: '#c4c6d0',
  divisor: 'rgba(255,255,255,0.10)',
  suave: 'rgba(42,99,255,0.14)',
  campo: '#10141d',
  campoBorde: '#8e9099',
  barra: '#0a1733',
  barraTexto: '#90befe',
  error: '#ffb4ab',
  sombra: 'rgba(42, 99, 255, 0.35)',
  estado: {
    gris: ['#2c3338', '#cfd8dc'],
    azul: ['#0d2a4a', '#9ccbf7'],
    amarillo: ['#3a2f00', '#ffe082'],
    verde: ['#12331a', '#a5d6a7'],
    naranja: ['#3e2100', '#ffb870'],
    rojo: ['#3d1214', '#f4a3a3'],
  },
};

export type Colores = typeof claro;
export type ColorEstado = keyof Colores['estado'];

export function useColores(): Colores {
  return useColorScheme() === 'dark' ? oscuro : claro;
}

export const RADIO_TARJETA = 14;
export const RADIO_PILDORA = 28;
