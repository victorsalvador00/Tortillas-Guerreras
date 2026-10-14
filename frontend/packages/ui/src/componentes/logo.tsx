import { cn } from '../utils';

/**
 * Logotipo de Guerreras (por Familia Ochoa). Cada app sirve las imágenes en `/brand/` (generadas desde
 * `docs/marca/Guerreras_logo.png`); va sobre una placa clara como en la barra superior del SGO.
 */
export function Logo({ className, chico, titulo = 'Guerreras, por Familia Ochoa' }: { className?: string; chico?: boolean; titulo?: string }) {
  return (
    <img
      src={chico ? '/brand/logo-guerreras-sm.webp' : '/brand/logo-guerreras.webp'}
      alt={titulo}
      width={chico ? 300 : 720}
      height={chico ? 82 : 196}
      className={cn('block h-8 w-auto', className)}
    />
  );
}
